# RemoteFlow Windows ↔ Android Protocol

Version de protocole : \`remoteflow-jsonl/1\`

## Phase 4 — Appairage, identité et sécurité

- Transport actuel : TCP
- Port : \`8443\`
- Encodage : UTF-8
- Framing : une ligne JSON = un message
- Identité Windows persistante : ECDSA P-256
- Empreinte affichée : SHA-256 de la clé publique
- PIN local : 6 chiffres, hashé avec PBKDF2-HMAC-SHA256 et protégé localement par Windows DPAPI
- QR compatible avec le client Android actuel : \`remoteflow://IP:8443\`
- Le serveur signe désormais son \`hello\` avec un nonce et sa clé d'identité.

### Hello serveur → Android

Exemple :

\`\`\`json
{"event":"hello","version":1,"product":"RemoteFlow","platform":"windows","port":8443,"protocol":"remoteflow-jsonl/1","deviceId":"...","fingerprint":"AA:BB:...","publicKey":"...","nonce":"...","signature":"...","pairingRequired":false,"pinLength":6,"security":"identity+p256"}
\`\`\`

Le client Android v1 peut ignorer les nouveaux champs et continuer à ouvrir la connexion TCP.

### Appairage PIN

Un futur client compatible peut envoyer :

\`\`\`json
{"action":"pair","pin":"123456","clientDeviceId":"android-123","clientName":"Mon téléphone"}
\`\`\`

Réponse :

\`\`\`json
{"event":"pairing","ok":true,"action":"pair","protocol":"remoteflow-jsonl/1","paired":true,"deviceId":"..."}
\`\`\`

### Verrouillage

Lorsque \`pairingRequired=true\`, les commandes \`mouse\`, \`keyboard\`, \`macro\`, \`whiteboard\` et \`clipboard\` sont refusées jusqu'à un appairage PIN valide pour la session.

Le verrouillage est volontairement **désactivé par défaut** dans cette phase pour conserver la compatibilité avec le client Android existant, qui ne possède pas encore l'étape \`action=pair\`.

## Compatibilité et limite actuelle

Cette phase ne prétend pas fournir un tunnel chiffré de bout en bout avec l'Android actuel : celui-ci ouvre encore un socket TCP et marque la session comme chiffrée sans négocier de TLS/clé de session. Windows prépare l'identité, le PIN, la signature de hello et le contrôle d'accès pour la prochaine évolution coordonnée du protocole.

Les actions reçues restent journalisées et accusées réception ; leur exécution système sera branchée dans les phases fonctionnelles suivantes.


## Phase 5 — Souris + clavier

Les actions \`mouse\` et \`keyboard\` sont maintenant exécutées réellement par Windows via \`user32.dll\` / \`SendInput\`.

La souris accepte \`MOVE\`, \`MOVE_RELATIVE\`, \`LEFT_CLICK\`, \`RIGHT_CLICK\`, \`DOUBLE_CLICK\`, \`SCROLL_UP\` et \`SCROLL_DOWN\`. Les coordonnées \`x/y\` sont normalisées de 0 à 1 sur le bureau virtuel Windows.

Le clavier exécute les caractères simples et les touches système courantes : Ctrl, Alt, Shift, Windows, Entrée, Échap, Tab, Espace, flèches, Home/End, PageUp/PageDown, Insert/Delete, CapsLock et F1–F12.

Les entrées sont injectées avec \`SendInput\` dans la fenêtre actuellement au premier plan. Les autres actions restent en contrat/ACK et seront implémentées dans leurs phases respectives.


## Phase 6 — Streaming du bureau

Windows peut maintenant capturer le bureau virtuel multi-écrans, compresser chaque image en JPEG et l'envoyer au client par JSONL.

Demarrage : \`{"action":"screen","type":"START","fps":8,"maxWidth":1280,"quality":60}\`.

Arret : \`{"action":"screen","type":"STOP"}\`.

Chaque frame est envoyee sous \`event=screen_frame\` avec \`sequence\`, \`timestamp\`, \`width\`, \`height\`, \`format=jpeg\`, \`quality\` et \`data\` en Base64. Le flux est limite a 15 FPS maximum pour conserver un serveur Windows stable.

Le client Android v1 actuel ne decode pas encore \`screen_frame\`; aucun fichier Android n'est donc modifie dans cette phase. Le serveur et le protocole de streaming sont prets pour le branchement de l'affichage distant Android.


## Phase 7 — Transfert de fichiers

Le serveur Windows gere les fichiers distants dans \`%USERPROFILE%\\Downloads\\RemoteFlow\`.

Commandes JSONL : \`LIST\`, \`UPLOAD_START\`, \`UPLOAD_CHUNK\`, \`UPLOAD_END\`, \`UPLOAD_CANCEL\`, \`DOWNLOAD_START\`, \`DOWNLOAD_CANCEL\`.

Les uploads utilisent des chunks Base64 de 32 KiB par defaut (128 KiB maximum), verifient strictement l'offset et utilisent un fichier \`.rfpart\` pour permettre une reprise depuis l'offset existant. Le fichier partiel n'est finalise qu'apres verification exacte de la taille.

Les downloads envoient \`file_chunk\` avec \`transferId\`, \`offset\`, \`totalBytes\`, \`final\` et \`data\`. Ils peuvent etre annules sans fermer la connexion.

Les chemins sont confines au dossier RemoteFlow afin d'empecher la traversee de repertoires, et les fichiers \`.rfpart\` ne sont pas exposes dans \`LIST\`.

L'Android v1 actuel n'envoie pas encore ces commandes de transfert ; aucun fichier Android n'est modifie dans cette phase.

## Phase 8 — Presse-papiers universel

Windows surveille nativement le presse-papiers texte toutes les 400 ms via Win32, sans composant cloud ni API Internet.

### Android → Windows

Le client peut envoyer :

\`\`\`json
{"action":"clipboard","text":"Bonjour depuis Android"}
\`\`\`

Windows place immédiatement le texte dans le presse-papiers système et répond par un ACK. La taille est limitée à 1 000 000 caractères afin d'éviter les messages JSONL démesurés.

### Windows → clients RemoteFlow compatibles

Lorsqu'un texte est copié localement sur Windows, le serveur diffuse :

\`\`\`json
{"event":"clipboard","text":"Bonjour depuis Windows","source":"windows","timestamp":0}
\`\`\`

Le champ \`timestamp\` contient l'heure Unix en millisecondes.

La diffusion est restreinte aux sessions autorisées lorsque le verrouillage par appairage est actif. Le changement provenant d'Android est marqué comme déjà observé pour éviter une boucle d'écho immédiate.

### Hors ligne / réseau local

La synchronisation reste locale au socket TCP RemoteFlow sur le réseau local. Aucun serveur distant n'est requis et aucune donnée de presse-papiers n'est envoyée à Internet par cette fonctionnalité.

Le client Android actuel sait déjà envoyer \`action=clipboard\`; l'application Android v1 ne consomme toutefois pas encore l'événement entrant \`event=clipboard\`. Aucun fichier Android n'est modifié dans cette phase.
