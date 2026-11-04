## do once
## gh auth refresh --hostname github.com -s write:packages
gh auth token --hostname github.com | docker login ghcr.io -u maythamfahmi --password-stdin
docker build -t ghcr.io/maythamfahmi/companyenrichment:latest .
docker push ghcr.io/maythamfahmi/companyenrichment:latest