# APT repository distribution

## Decision

CSharpFar publishes its Debian packages through a signed static APT repository
on GitHub Pages at `https://dimonsmart.github.io/CSharpFar/apt/`. GitHub Releases
remain the source of package history; the Pages workflow rebuilds the repository
from every non-draft `csharpfar_*_amd64.deb` release asset so rerunning it does
not discard older package versions.

The Debian package is built from the existing verified self-contained
`linux-x64` publish output. This keeps the portable archive and Debian package
on one Unix build model and installs the existing `csharpfar` executable as
`/usr/bin/csharpfar`.

The repository uses suite `stable` and component `main`. CSharpFar currently
has one release channel and one directly maintained component, so additional
suites or components would add distinctions that the release process does not
yet provide.

Only `amd64` is published because the release matrix currently produces only
`linux-x64`. Another Debian architecture should be added only with its matching
Unix publish runtime and release asset.

## Signing and recovery

The dedicated repository private key and optional passphrase are stored as the
GitHub Actions secrets `APT_GPG_PRIVATE_KEY` and `APT_GPG_PASSPHRASE`. Private
key material is not stored in git, GitHub Releases, or the Pages artifact. The
workflow exports only the public key into the repository.

If the automatic run after `Release` fails or Pages must be rebuilt, run the
`APT Pages` workflow manually with `workflow_dispatch`. It redownloads all
matching Debian release assets, regenerates and signs the complete repository,
and deploys one Pages artifact containing the site root and `apt/` subtree.

GitHub Pages must be configured separately to use GitHub Actions as its source.
If documentation is later published on the same Pages site, its generated files
must be merged into this same site artifact before deployment.
