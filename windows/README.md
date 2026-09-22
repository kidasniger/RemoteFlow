# RemoteFlow Windows

Application Windows native dédiée à RemoteFlow.

Cette partie est indépendante du projet Android. Le dossier `windows/` n'embarque pas `RemoteFlow.html`. La maquette HTML sert uniquement de référence visuelle.

## Structure
- `src/RemoteFlow.Windows` : application Windows native WPF.
- `src/RemoteFlow.Windows/Core` : cœur métier et contrats de communication.
- `installer` : script Inno Setup.
- `.github/workflows/windows-build.yml` : build et release Windows.

## Développement
Prérequis : Windows, .NET 8 SDK et Inno Setup 6.

```powershell
dotnet build .\\windows\\RemoteFlow.Windows.sln -c Release
```
