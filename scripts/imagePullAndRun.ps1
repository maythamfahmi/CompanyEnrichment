## do once
## gh auth refresh --hostname github.com -s read:packages
gh auth token --hostname github.com | docker login ghcr.io -u maythamfahmi --password-stdin
docker pull ghcr.io/maythamfahmi/companyenrichment:latest
docker run --rm -p 8080:8080 ghcr.io/maythamfahmi/companyenrichment:latest