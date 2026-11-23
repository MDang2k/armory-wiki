# Deploying ArmoryWiki

One VPS running Docker Compose behind Caddy ([ADR-0002](../docs/adr/0002-single-vps-same-origin.md), PLAN.md Phase 6).
Pushes to `main` build the images on GitHub Actions, publish them to GHCR and deploy them; this guide covers the
one-time server setup, the commands you run by hand, and backups.

| File | Purpose |
|---|---|
| `docker-compose.prod.yml` | Caddy, the Next.js frontend (`web`), the API, Postgres; volumes for media, keys and the database |
| `Caddyfile` | TLS, `/api/*` → API, `/media/*` → the media volume, everything else → frontend |
| `.env.example` | Secrets and settings for the stack (copy to `.env`) |
| `backup.sh`, `restore-check.sh` | Nightly backup to restic and the restore proof |
| `backup.env.example` | Backup repository and password (copy to `backup.env`) |
| `systemd/` | The nightly backup timer |

## 1. Server (once)

A small VPS is plenty (e.g. Hetzner CX22: 2 vCPU, 4 GB, Ubuntu 24.04). As root:

```bash
apt update && apt full-upgrade -y
apt install -y unattended-upgrades ufw
dpkg-reconfigure -plow unattended-upgrades          # security updates on their own

# Docker Engine and the compose plugin: https://docs.docker.com/engine/install/ubuntu/

adduser --disabled-password --gecos "" deploy
usermod -aG docker deploy                            # docker group: deploys and backups need no sudo
install -d -o deploy -g deploy -m 750 /opt/armorywiki
install -d -o deploy -g deploy -m 700 /home/deploy/.ssh
# Put your own public key and the CI deploy public key in /home/deploy/.ssh/authorized_keys (chmod 600).

ufw allow 22/tcp && ufw allow 80/tcp && ufw allow 443/tcp && ufw allow 443/udp && ufw enable
```

In `/etc/ssh/sshd_config`: `PasswordAuthentication no` and `PermitRootLogin no`, then `systemctl reload ssh`.
Check you can still log in as `deploy` in a second session before closing the first.

Point the domain's A (and AAAA) record at the server before the first start: Caddy requests the certificate then.

## 2. Configuration (once)

As `deploy`, in `/opt/armorywiki`:

```bash
cp .env.example .env && chmod 600 .env               # copy .env.example from the repo first
openssl rand -hex 24                                 # for POSTGRES_PASSWORD (letters and digits only)
openssl rand -hex 32                                 # for REVALIDATE_SECRET
```

Fill in `.env`: `SITE_DOMAIN`, the Postgres password, the admin email and password (12+ characters with upper
case, a digit and a symbol), `REVALIDATE_SECRET`, and `YOUTUBE_API_KEY` if you will import videos.

## 3. GitHub (once)

In the repository's **Settings → Secrets and variables → Actions**, add:

| Secret | Value |
|---|---|
| `DEPLOY_HOST` | the server's address |
| `DEPLOY_USER` | `deploy` |
| `DEPLOY_SSH_KEY` | a private key made for CI only (`ssh-keygen -t ed25519 -f ci-deploy -N ""`); its public half goes in `authorized_keys` |
| `DEPLOY_KNOWN_HOSTS` | the output of `ssh-keyscan <server>`, checked against the server's own fingerprint (`ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub`) |

The next push to `main` (or **Actions → CI → Run workflow**) copies the compose file, Caddyfile and backup scripts,
pulls the images, migrates and starts the stack. The images on GHCR are public, so the server needs no registry login.

## 4. By hand

```bash
cd /opt/armorywiki
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f api

# Imports (PLAN.md Phase 4). Reports land in import-reports/. Try --dry-run first.
docker compose -f docker-compose.prod.yml run --rm api import-wikidata --dry-run
docker compose -f docker-compose.prod.yml run --rm api import-wikidata
docker compose -f docker-compose.prod.yml run --rm api import-commons
docker compose -f docker-compose.prod.yml run --rm api import-wikipedia
docker compose -f docker-compose.prod.yml run --rm api import-fw-videos   # needs YOUTUBE_API_KEY

# Roll back to an earlier build: any commit SHA that CI built.
IMAGE_TAG=<sha> docker compose -f docker-compose.prod.yml up -d
```

## 5. Backups

Every night `backup.sh` dumps the database (`pg_dump`, custom format) and backs it up with the media volume into a
[restic](https://restic.net) repository on Backblaze B2: encrypted, deduplicated, keeping 7 daily, 4 weekly and 6
monthly snapshots, with a structural check on Sundays. The `keys` volume is left out on purpose (losing it only
signs the admin out; leaking it would allow forged admin cookies), and so are Caddy's certificates (it gets new ones).

Set it up once:

1. Create a **private** B2 bucket and an application key limited to it.
2. In `/opt/armorywiki`: `cp backup.env.example backup.env && chmod 600 backup.env`, and fill it in. Keep
   `RESTIC_PASSWORD` somewhere off the server too (a password manager): without it no backup can be restored.
3. Create the repository, once (the nightly run never does, so a mistyped repository fails instead of starting afresh):
   `docker run --rm --env-file backup.env restic/restic:0.19.1 init`
4. For an alert when a night fails or is missed, create a check at healthchecks.io (free; schedule daily, grace 2 hours)
   and put its ping URL in `HEALTHCHECK_URL`.
5. As root:
   ```bash
   cp /path/to/repo/deploy/systemd/armorywiki-backup.* /etc/systemd/system/
   systemctl daemon-reload
   systemctl enable --now armorywiki-backup.timer
   systemctl start armorywiki-backup.service          # a first backup now
   journalctl -u armorywiki-backup.service            # its output
   ```

### Proving a restore

```bash
cd /opt/armorywiki
./restore-check.sh                 # the latest snapshot; or ./restore-check.sh <snapshot-id>
```

It restores the snapshot into a scratch database (`armorywiki_restore_check`) and a scratch folder, compares row and
file counts with the live site, and cleans up (`KEEP=1` keeps them for a look). It never touches the live data. Run it
after setting backups up, and every few months.

### Restoring for real

After losing the server, on a new one set up as in sections 1–3 (with the same `.env` and `backup.env`):

```bash
cd /opt/armorywiki
docker compose -f docker-compose.prod.yml up --no-start      # creates the volumes, so compose owns them
docker compose -f docker-compose.prod.yml start postgres
docker run --rm --env-file backup.env --hostname armorywiki --user "$(id -u):$(id -g)" -e RESTIC_CACHE_DIR=/tmp/restic-cache -v "$PWD/restore:/restore" restic/restic:0.19.1 restore latest --target /restore
docker compose -f docker-compose.prod.yml exec -T postgres sh -c 'pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --no-owner --clean --if-exists' < restore/data/db/db.dump
docker run --rm -v "$PWD/restore/data/media:/from:ro" -v armorywiki_media:/to busybox:1.37 sh -c 'cp -a /from/. /to/ && chown -R 1654:1654 /to'
docker compose -f docker-compose.prod.yml run --rm api migrate
docker compose -f docker-compose.prod.yml up -d
rm -r restore                                      # the restored copy is no longer needed
```

## 6. Monitoring (optional)

The containers restart on failure and report health (`docker compose ps`). For an alert when the site is down, add
the domain to a free uptime monitor such as UptimeRobot.
