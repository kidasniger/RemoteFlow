using System.IO;
using System.Net.Sockets;
using RemoteFlow.Windows.Core;
using RemoteFlow.Windows.Network;

namespace RemoteFlow.Windows.Files;

public sealed class FileTransferManager
{
    public const int DefaultChunkSize = 32 * 1024;
    public const int MaxChunkSize = 128 * 1024;
    public const int MaxListedFiles = 500;

    private readonly string _rootPath;

    public event EventHandler<RemoteFlowFileTransferState>? TransferStatusChanged;

    public FileTransferManager()
    {
        _rootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads",
            "RemoteFlow");
        Directory.CreateDirectory(_rootPath);
    }

    public string RootPath => _rootPath;

    public IReadOnlyList<RemoteFlowFileInfo> ListFiles()
    {
        var result = new List<RemoteFlowFileInfo>();

        foreach (var file in Directory.EnumerateFiles(_rootPath, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".rfpart", StringComparison.OrdinalIgnoreCase) ||
                file.Contains($"{Path.DirectorySeparatorChar}.", StringComparison.Ordinal))
                continue;

            if (result.Count >= MaxListedFiles)
                break;

            var info = new FileInfo(file);
            result.Add(new RemoteFlowFileInfo(
                Name: info.Name,
                RelativePath: Path.GetRelativePath(_rootPath, file).Replace(Path.DirectorySeparatorChar, '/'),
                Size: info.Length,
                LastModifiedUtc: info.LastWriteTimeUtc));
        }

        return result;
    }


    public string GetLocalFilePath(string relativePath) => ResolveLocalPath(relativePath);

    public void DeleteFile(string relativePath)
    {
        var path = ResolveLocalPath(relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Fichier introuvable.", relativePath);

        File.Delete(path);
    }

    public void ImportFile(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            throw new FileNotFoundException("Fichier source introuvable.", sourcePath);

        var targetName = Path.GetFileName(sourcePath);
        var targetPath = ResolveLocalPath(targetName);
        File.Copy(sourcePath, targetPath, true);
    }

    public FileTransferSession CreateSession() =>
        new(_rootPath, state => _transferStatusChanged?.Invoke( state));

    private string ResolveLocalPath(string requestedPath)
    {
        var normalized = requestedPath
            .Replace((char)92, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
            throw new InvalidDataException("Nom de fichier vide.");

        var rootFull = Path.GetFullPath(_rootPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(_rootPath, normalized));

        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Chemin de fichier interdit.");

        return candidate;
    }

    public sealed class FileTransferSession : IAsyncDisposable
    {
        private readonly string _rootPath;
        private readonly Dictionary<string, UploadState> _uploads = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CancellationTokenSource> _downloads = new(StringComparer.Ordinal);
        private readonly object _gate = new();
        private readonly Action<RemoteFlowFileTransferState>? _transferStatusChanged;

        internal FileTransferSession(
            string rootPath,
            Action<RemoteFlowFileTransferState>? transferStatusChanged)
        {
            _rootPath = rootPath;
            _transferStatusChanged = transferStatusChanged;
        }

        public Task<RemoteFlowFileTransferState> StartUploadAsync(
            string transferId,
            string fileName,
            long size,
            long requestedOffset,
            CancellationToken cancellationToken)
        {
            ValidateTransferId(transferId);

            if (size < 0)
                throw new InvalidDataException("Taille de fichier invalide.");

            var finalPath = ResolveSafePath(fileName);
            var partPath = finalPath + ".rfpart";
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            CancelUploadInternal(transferId);

            long offset = 0;
            FileStream stream;

            if (requestedOffset > 0)
            {
                if (!File.Exists(partPath))
                    throw new InvalidDataException("Aucun fichier partiel disponible pour reprendre le transfert.");

                var existingLength = new FileInfo(partPath).Length;
                if (existingLength != requestedOffset || existingLength > size)
                    throw new InvalidDataException(
                        $"Offset de reprise invalide. Partiel={existingLength}, demandé={requestedOffset}, taille={size}.");

                offset = requestedOffset;
                stream = new FileStream(
                    partPath,
                    FileMode.Open,
                    FileAccess.Write,
                    FileShare.None,
                    DefaultChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                stream.Position = offset;
            }
            else
            {
                stream = new FileStream(
                    partPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    DefaultChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
            }

            lock (_gate)
            {
                _uploads[transferId] = new UploadState(
                    transferId,
                    fileName,
                    finalPath,
                    partPath,
                    size,
                    offset,
                    stream);
            }

            var startedState = new RemoteFlowFileTransferState(
                Event: "file_transfer",
                TransferId: transferId,
                State: offset == 0 ? "started" : "resumed",
                Offset: offset,
                TotalBytes: size,
                FileName: fileName);
            _transferStatusChanged?.Invoke( startedState);
            return Task.FromResult(startedState);
        }

        public async Task<RemoteFlowFileTransferState> WriteChunkAsync(
            string transferId,
            long offset,
            string data,
            CancellationToken cancellationToken)
        {
            if (!TryGetUpload(transferId, out var upload))
                throw new InvalidOperationException("Transfert inexistant.");

            if (offset != upload.Offset)
                throw new InvalidDataException(
                    $"Offset inattendu. Attendu {upload.Offset}, reçu {offset}.");

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(data);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("Chunk Base64 invalide.");
            }

            if (bytes.Length == 0)
                throw new InvalidDataException("Chunk vide.");

            if (bytes.Length > MaxChunkSize)
                throw new InvalidDataException($"Chunk trop grand : {bytes.Length} octets.");

            var remaining = upload.TotalBytes - upload.Offset;
            if (bytes.LongLength > remaining)
                throw new InvalidDataException("Le chunk dépasse la taille annoncée.");

            await upload.Stream.WriteAsync(bytes, cancellationToken);
            upload.Offset += bytes.LongLength;

            var progressState = new RemoteFlowFileTransferState(
                Event: "file_transfer",
                TransferId: transferId,
                State: "progress",
                Offset: upload.Offset,
                TotalBytes: upload.TotalBytes,
                FileName: upload.FileName);
            _transferStatusChanged?.Invoke( progressState);
            return progressState;
        }

        public async Task<RemoteFlowFileTransferState> FinishUploadAsync(
            string transferId,
            CancellationToken cancellationToken)
        {
            if (!TryGetUpload(transferId, out var upload))
                throw new InvalidOperationException("Transfert inexistant.");

            if (upload.Offset != upload.TotalBytes)
                throw new InvalidDataException(
                    $"Fichier incomplet : {upload.Offset}/{upload.TotalBytes} octets.");

            await upload.Stream.FlushAsync(cancellationToken);
            await upload.Stream.DisposeAsync();

            lock (_gate)
            {
                _uploads.Remove(transferId);
            }

            File.Move(upload.PartPath, upload.FinalPath, true);

            var completedState = new RemoteFlowFileTransferState(
                Event: "file_transfer",
                TransferId: transferId,
                State: "completed",
                Offset: upload.Offset,
                TotalBytes: upload.TotalBytes,
                FileName: upload.FileName);
            _transferStatusChanged?.Invoke( completedState);
            return completedState;
        }

        public Task CancelUploadAsync(string transferId)
        {
            ValidateTransferId(transferId);
            CancelUploadInternal(transferId, deletePart: true);
            return Task.CompletedTask;
        }

        public async Task<RemoteFlowFileTransferState> StartDownloadAsync(
            JsonLineSession session,
            string transferId,
            string fileName,
            long offset,
            CancellationToken sessionCancellation)
        {
            ValidateTransferId(transferId);
            var safePath = ResolveSafePath(fileName);

            if (!File.Exists(safePath))
                throw new FileNotFoundException("Fichier introuvable.", fileName);

            if (offset < 0)
                throw new InvalidDataException("Offset de téléchargement invalide.");

            CancellationTokenSource cts;
            lock (_gate)
            {
                CancelDownloadInternal(transferId);
                cts = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation);
                _downloads[transferId] = cts;
            }

            var fileInfo = new FileInfo(safePath);
            if (offset > fileInfo.Length)
            {
                CancelDownloadInternal(transferId);
                cts.Dispose();
                throw new InvalidDataException("Offset de téléchargement supérieur à la taille du fichier.");
            }

            await session.SendAsync(
                new RemoteFlowFileTransferState(
                    Event: "file_transfer",
                    TransferId: transferId,
                    State: "started",
                    Offset: offset,
                    TotalBytes: fileInfo.Length,
                    FileName: fileName),
                sessionCancellation);

            _ = RunDownloadAsync(
                session,
                transferId,
                safePath,
                fileName,
                offset,
                cts);

            var downloadStartedState = new RemoteFlowFileTransferState(
                Event: "file_transfer",
                TransferId: transferId,
                State: "started",
                Offset: offset,
                TotalBytes: fileInfo.Length,
                FileName: fileName);
            _transferStatusChanged?.Invoke( downloadStartedState);
            return downloadStartedState;
        }

        public Task CancelDownloadAsync(string transferId)
        {
            ValidateTransferId(transferId);
            lock (_gate)
            {
                CancelDownloadInternal(transferId);
            }
            return Task.CompletedTask;
        }

        private async Task RunDownloadAsync(
            JsonLineSession session,
            string transferId,
            string safePath,
            string fileName,
            long offset,
            CancellationTokenSource cts)
        {
            try
            {
                await using var stream = new FileStream(
                    safePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    DefaultChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);

                stream.Position = offset;
                var totalBytes = stream.Length;
                var buffer = new byte[DefaultChunkSize];
                var currentOffset = offset;

                while (!cts.IsCancellationRequested && currentOffset < totalBytes)
                {
                    var read = await stream.ReadAsync(buffer, cts.Token);
                    if (read == 0)
                        break;

                    await session.SendAsync(
                        new RemoteFlowFileChunk(
                            Event: "file_chunk",
                            TransferId: transferId,
                            Offset: currentOffset,
                            TotalBytes: totalBytes,
                            Final: currentOffset + read >= totalBytes,
                            Data: Convert.ToBase64String(buffer, 0, read)),
                        cts.Token);

                    currentOffset += read;
                    TransferStatusChanged?.Invoke(
                        this,
                        new RemoteFlowFileTransferState(
                            Event: "file_transfer",
                            TransferId: transferId,
                            State: "progress",
                            Offset: currentOffset,
                            TotalBytes: totalBytes,
                            FileName: fileName));
                }

                if (!cts.IsCancellationRequested)
                {
                    var completedState = new RemoteFlowFileTransferState(
                        Event: "file_transfer",
                        TransferId: transferId,
                        State: "completed",
                        Offset: currentOffset,
                        TotalBytes: totalBytes,
                        FileName: fileName);
                    await session.SendAsync(completedState, cts.Token);
                    _transferStatusChanged?.Invoke( completedState);
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    var cancelledState = new RemoteFlowFileTransferState(
                        Event: "file_transfer",
                        TransferId: transferId,
                        State: "cancelled",
                        Offset: offset,
                        TotalBytes: 0,
                        FileName: fileName);
                    await session.SendAsync(cancelledState, CancellationToken.None);
                    _transferStatusChanged?.Invoke( cancelledState);
                }
                catch
                {
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    if (_downloads.Remove(transferId, out var owned) && ReferenceEquals(owned, cts))
                        owned.Dispose();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            List<FileStream> uploadStreams;
            List<CancellationTokenSource> downloadSources;

            lock (_gate)
            {
                uploadStreams = _uploads.Values.Select(x => x.Stream).ToList();
                _uploads.Clear();

                downloadSources = _downloads.Values.ToList();
                _downloads.Clear();
            }

            foreach (var source in downloadSources)
            {
                try { source.Cancel(); } catch { }
                source.Dispose();
            }

            foreach (var stream in uploadStreams)
            {
                try { await stream.DisposeAsync(); } catch { }
            }
        }

        private bool TryGetUpload(string transferId, out UploadState upload)
        {
            lock (_gate)
            {
                return _uploads.TryGetValue(transferId, out upload!);
            }
        }

        private void CancelUploadInternal(string transferId, bool deletePart = false)
        {
            UploadState? upload;
            lock (_gate)
            {
                if (!_uploads.Remove(transferId, out upload))
                    return;
            }

            try { upload.Stream.Dispose(); } catch { }

            if (deletePart)
            {
                try
                {
                    if (File.Exists(upload.PartPath))
                        File.Delete(upload.PartPath);
                }
                catch
                {
                }
            }
        }

        private void CancelDownloadInternal(string transferId)
        {
            if (_downloads.Remove(transferId, out var cts))
            {
                try { cts.Cancel(); } catch { }
                cts.Dispose();
            }
        }

        private string ResolveSafePath(string requestedPath)
        {
            var normalized = requestedPath
                .Replace((char)92, Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar)
                .Trim();

            if (string.IsNullOrWhiteSpace(normalized))
                throw new InvalidDataException("Nom de fichier vide.");

            var rootFull = Path.GetFullPath(_rootPath)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(_rootPath, normalized));

            if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("Chemin de fichier interdit.");

            return candidate;
        }

        private static void ValidateTransferId(string transferId)
        {
            if (string.IsNullOrWhiteSpace(transferId) || transferId.Length > 100)
                throw new InvalidDataException("Identifiant de transfert invalide.");

            if (transferId.Any(c => !char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new InvalidDataException("Identifiant de transfert invalide.");
        }

        private sealed class UploadState
        {
            public UploadState(
                string transferId,
                string fileName,
                string finalPath,
                string partPath,
                long totalBytes,
                long offset,
                FileStream stream)
            {
                TransferId = transferId;
                FileName = fileName;
                FinalPath = finalPath;
                PartPath = partPath;
                TotalBytes = totalBytes;
                Offset = offset;
                Stream = stream;
            }

            public string TransferId { get; }
            public string FileName { get; }
            public string FinalPath { get; }
            public string PartPath { get; }
            public long TotalBytes { get; }
            public long Offset { get; set; }
            public FileStream Stream { get; }
        }
    }
}
