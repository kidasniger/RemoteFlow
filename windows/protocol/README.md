# RemoteFlow Windows ↔ Android Protocol

Version de protocole : `remoteflow-jsonl/1`

## Phase 4 — Appairage, identité et sécurité

- Transport actuel : TCP
- Port : `8443`
- Encodage : UTF-8
- Framing : une ligne JSON = un message
- Identité Windows persistante : ECDSA P-256
- Empreinte affichée : SHA-256 de la clé publique
- PIN local : 6 chiffres, hashé avec PBKDF2-HMAC-SHA256 et protégé localement par Windows DPAPI
- QR compatible avec le client Android actuel : `remoteflow://IP:8443`
- Le serveur signe désormais son `hello` avec un nonce et sa clé d'identité.

### Hello serveur → Android

Exemple :

```json
{"event":"hello","version":1,"product":"RemoteFlow","platform":"windows","port":8443,"protocol":"remoteflow-jsonl/1","deviceId":"...","fingerprint":"AA:BB:...","publicKey":"...","nonce":"...","signature":"...","pairingRequired":false,"pinLength":6,"security":"identity+p256"}
```

Le client Android v1 peut ignorer les nouveaux champs et continuer à ouvrir la connexion TCP.

### Appairage PIN

Un futur client compatible peut envoyer :

```json
{"action":"pair","pin":"123456","clientDeviceId":"android-123","clientName":"Mon téléphone"}
```

Réponse :

```json
{"event":"pairing","ok":true,"action":"pair","protocol":"remoteflow-jsonl/1","paired":true,"deviceId":"..."}
```

### Verrouillage

Lorsque `pairingRequired=true`, les commandes `mouse`, `keyboard`, `macro`, `whiteboard` et `clipboard` sont refusées jusqu'à un appairage PIN valide pour la session.

Le verrouillage est volontairement **désactivé par défaut** dans cette phase pour conserver la compatibilité avec le client Android existant, qui ne possède pas encore l'étape `action=pair`.

## Compatibilité et limite actuelle

Cette phase ne prétend pas fournir un tunnel chiffré de bout en bout avec l'Android actuel : celui-ci ouvre encore un socket TCP et marque la session comme chiffrée sans négocier de TLS/clé de session. Windows prépare l'identité, le PIN, la signature de hello et le contrôle d'accès pour la prochaine évolution coordonnée du protocole.

Les actions reçues restent journalisées et accusées réception ; leur exécution système sera branchée dans les phases fonctionnelles suivantes.
