# Deployment

## Data Protection Certificate with cert-manager (Kubernetes)

[cert-manager](https://cert-manager.io/) can provision and rotate the PKCS#12 certificate used to encrypt the Data Protection key ring. This removes the need to manage PFX files manually.

### Prerequisites

- cert-manager installed in the cluster

### 1. Create the PKCS#12 password Secret

cert-manager reads this secret when building the PKCS#12 keystore — it must exist before the Certificate resource:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: dp-key-encryption-password
  namespace: obsidian-sync
type: Opaque
stringData:
  password: <generate-a-random-password>
```

### 2. Create a self-signed Issuer

A self-signed issuer is sufficient here — the certificate is only used for local key encryption, not TLS:

```yaml
apiVersion: cert-manager.io/v1
kind: Issuer
metadata:
  name: dp-selfsigned
  namespace: obsidian-sync
spec:
  selfSigned: {}
```

### 3. Request the Certificate

The `keystores.pkcs12` section tells cert-manager to produce a `.p12` file inside the resulting Secret:

```yaml
apiVersion: cert-manager.io/v1
kind: Certificate
metadata:
  name: dp-key-encryption
  namespace: obsidian-sync
spec:
  secretName: dp-key-encryption-tls
  issuerRef:
    name: dp-selfsigned
    kind: Issuer
  commonName: obsidian-sync-manager-dp
  duration: 8760h    # 1 year
  renewBefore: 720h  # 30 days
  privateKey:
    algorithm: RSA
    size: 2048
  keystores:
    pkcs12:
      create: true
      passwordSecretRef:
        name: dp-key-encryption-password
        key: password
```

After applying, cert-manager creates the `dp-key-encryption-tls` Secret containing:

| Key | Contents |
|-----|----------|
| `tls.crt` | PEM certificate |
| `tls.key` | PEM private key |
| `ca.crt` | CA certificate (same as `tls.crt` for self-signed) |
| `keystore.p12` | PKCS#12 bundle (used by the app) |

### 4. Mount into the pod

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: obsidian-sync-manager
  namespace: obsidian-sync
spec:
  template:
    spec:
      volumes:
        - name: dp-cert
          secret:
            secretName: dp-key-encryption-tls
      containers:
        - name: web
          image: ghcr.io/twsouthwick/obsidian-sync-manager:latest
          volumeMounts:
            - name: dp-cert
              mountPath: /etc/dp-cert
              readOnly: true
          env:
            - name: DataProtection__CertificatePath
              value: /etc/dp-cert/keystore.p12
            - name: DataProtection__CertificatePassword
              valueFrom:
                secretKeyRef:
                  name: dp-key-encryption-password
                  key: password
```

### Certificate Rotation

cert-manager renews the certificate automatically based on `renewBefore`. When the Secret is updated:

- Kubernetes updates the mounted volume (may take up to 60s depending on kubelet sync period)
- The pod must be restarted to load the new certificate — use a rolling restart or a sidecar like [reloader](https://github.com/stakater/Reloader) to automate this
- Existing Data Protection keys encrypted with the previous certificate remain readable — the old key ring entries still decrypt successfully as long as the key ring XML is intact
- New keys will be encrypted with the renewed certificate

## Filesystem Key Storage

By default, Data Protection keys are stored in the CouchDB `data-protection-keys` database. To store them on the filesystem instead, set the `DataProtection__KeyStorePath` environment variable to a directory path. This is useful when you want to keep key material outside of CouchDB — for example, on an encrypted volume or a persistent volume that is backed up separately.

When `DataProtection__KeyStorePath` is set, the CouchDB `data-protection-keys` database is not used. Certificate encryption (`DataProtection__CertificatePath`) still applies regardless of storage backend.

### Kubernetes example

```yaml
volumes:
  - name: dp-keys
    persistentVolumeClaim:
      claimName: dp-keys-pvc
containers:
  - name: web
    volumeMounts:
      - name: dp-keys
        mountPath: /var/lib/dp-keys
    env:
      - name: DataProtection__KeyStorePath
        value: /var/lib/dp-keys
```

> **Warning:** Loss of this directory means all Data Protection–encrypted values (HMAC secret, E2EE passphrases) become unrecoverable. Ensure the volume is backed up.
