# RemoteFlow Windows ↔ Android Protocol

Version de protocole : `remoteflow-jsonl/1`

## Transport Phase 3

- Transport : TCP
- Port par défaut : `8443`
- Encodage : UTF-8
- Framing : une ligne JSON = un message
- `TCP_NODELAY` activé côté Windows
- Le serveur écoute sur `0.0.0.0:8443`

Cette phase implémente le transport et le contrat de messages. **Le chiffrement/authentification forte est réservé à la Phase 4**.

## Hello serveur → Android

```json
{"event":"hello","version":1,"product":"RemoteFlow","platform":"windows","port":8443}
```

## Messages Android → Windows

### Souris
```json
{"action":"mouse","type":"MOVE","x":0.5,"y":0.4,"dx":0.02,"dy":-0.01}
```
Types actuellement attendus : `MOVE`, `LEFT_CLICK`, `RIGHT_CLICK`, `DOUBLE_CLICK`, `SCROLL_UP`, `SCROLL_DOWN`.

### Clavier
```json
{"action":"keyboard","key":"Ctrl","special":true,"modifier":true}
```

### Macro
```json
{"action":"macro","id":"macro-1","cmd":"open-terminal"}
```

### Tableau blanc
```json
{"action":"whiteboard","color":"#22D3EE","width":4.0,"pointsCount":18}
```

### Presse-papiers
```json
{"action":"clipboard","text":"Bonjour RemoteFlow"}
```

## ACK Windows → Android

```json
{"event":"ack","ok":true,"action":"mouse","protocol":"remoteflow-jsonl/1"}
```

Une trame JSON invalide reçoit `ok:false` avec un champ `error`.

## Compatibilité

Le serveur Windows accepte directement les trames actuellement envoyées par `RemotePcClient.kt` sans modification du projet Android. Les actions reçues sont journalisées et accusées réception ; leur exécution système sera branchée dans les phases fonctionnelles suivantes.
