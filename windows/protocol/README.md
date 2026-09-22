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


## Phase 13 — Tableau blanc Windows natif

Windows propose désormais un tableau blanc WPF natif basé sur `InkCanvas`.

Fonctions locales :
- dessin à la souris ou au stylet ;
- palette de couleurs ;
- épaisseur configurable ;
- annulation du dernier tracé ;
- effacement du tableau ;
- export du tableau en PNG ;
- envoi manuel de tous les tracés vers les clients RemoteFlow autorisés.

Le protocole accepte maintenant les tracés complets avec `points`, chaque point étant normalisé de 0 à 1, ainsi que `color`, `width`, `source` et `timestamp`.

Exemple :

```json
{"event":"whiteboard_stroke","points":[{"x":0.1,"y":0.2},{"x":0.2,"y":0.25}],"color":"#2563EB","width":6,"source":"windows","timestamp":0}
```

Windows conserve la compatibilité avec le format Android v1 actuel qui ne fournit que `pointsCount` : le tracé est accusé réception et journalisé, mais ne peut pas être redessiné sans coordonnées.

Les tracés complets provenant d'un client compatible sont affichés immédiatement dans le tableau blanc Windows puis relayés aux autres clients autorisés.


## Phase 14 — Webcam Windows native

Windows intègre désormais une capture webcam locale basée sur OpenCV/OpenCvSharp :

- détection des webcams disponibles ;
- sélection de la caméra ;
- aperçu vidéo local dans WPF ;
- résolution 640×480, 1280×720 ou 1920×1080 ;
- cadence configurable de 5 à 30 FPS ;
- qualité JPEG de 50 à 90 ;
- démarrage et arrêt natifs ;
- diffusion des images JPEG aux clients RemoteFlow autorisés lorsque la capture est active.

Commandes JSONL compatibles :

```json
{"action":"webcam","type":"START","cameraIndex":0,"frameWidth":1280,"frameHeight":720,"fps":15,"quality":70}
{"action":"webcam","type":"STOP"}
```

Le flux utilise :

```json
{"event":"webcam_stream","state":"started","cameraIndex":0,"cameraName":"Webcam 1","width":1280,"height":720,"fps":15,"quality":70}
```

puis des événements `webcam_frame` contenant l'image JPEG en Base64.

La version Android actuelle n'est pas modifiée dans cette phase ; elle devra décoder `webcam_frame` pour afficher le flux distant dans une prochaine évolution coordonnée.


## Phase 15 — Paramètres Windows natifs

La page **Paramètres & sécurité** est maintenant native WPF et persiste ses réglages dans :

`%APPDATA%\RemoteFlow\settings.json`

Réglages :
- port TCP RemoteFlow, limité à 1024–65535 ;
- verrouillage par appairage ;
- synchronisation du presse-papiers ;
- démarrage automatique avec Windows ;
- lancement réduit.

Le port est appliqué en redémarrant proprement le serveur TCP. Le démarrage automatique utilise la clé utilisateur Windows `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` et lance RemoteFlow avec l'option `--minimized`.

La réinitialisation remet les réglages applicatifs aux valeurs par défaut, désactive le démarrage automatique, désactive le verrouillage par appairage et réactive le presse-papiers.

## Phase 16 — Tableau de bord système réel

Le tableau de bord Windows n'utilise plus de valeurs CPU, RAM ou réseau fictives.

Les métriques affichées sont actualisées automatiquement chaque seconde :
- CPU système via GetSystemTimes ;
- mémoire physique totale, utilisée et disponible via GlobalMemoryStatusEx ;
- nombre réel de connexions TCP actives du serveur RemoteFlow ;
- état réel de la synchronisation du presse-papiers ;
- état du serveur TCP, port d'écoute et durée depuis le démarrage de Windows via GetTickCount64.

La carte auparavant dédiée à une latence fixe de démonstration affiche désormais le nombre réel de clients connectés. La latence réseau distante n'est pas inventée : le protocole actuel ne définit pas encore de mesure RTT client/serveur exploitable par le tableau de bord.

Aucune modification Android n'est requise pour cette phase.
## Phase 17 — Journal d'activité Windows

Une page WPF native `Journal d'activité` conserve un historique local des événements RemoteFlow dans `%APPDATA%\\RemoteFlow\\activity-log.json`.

Le journal est limité à 500 entrées et couvre les connexions/déconnexions, commandes distantes, transferts de fichiers, webcam, écran, tableau blanc, macro, presse-papiers, serveur et paramètres.

Le contenu du presse-papiers n'est jamais enregistré : seuls l'état et les métadonnées de synchronisation sont journalisés.

La page permet le filtrage par catégorie, l'effacement du journal et l'export CSV.