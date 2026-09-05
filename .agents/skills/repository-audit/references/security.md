# Security Audit

## Start With the Threat Model

State the relevant trust boundaries: remote metadata, local user input, update servers, filesystem, privileged installer paths, CI/release credentials, plugins/extensions, or multi-user data.

## Inspect

### Secrets

Search likely identifiers (`token`, `password`, `secret`, `private_key`, `authorization`) but verify whether values are actually confidential. Public protocol constants and placeholders are not secrets by default.

### Filesystem

Check traversal/canonicalization, archive extraction, symlink/reparse-point behavior, overwrite/delete boundaries, temp files, and privilege transitions.

### Process execution

Trace untrusted input into executable paths, arguments, shells, and OS URL handlers. Structured argument APIs are safer than shell string concatenation.

### Network

Inspect TLS validation, redirect policy, HTTPS downgrade, remote URL validation, SSRF boundaries, DNS rebinding exposure, proxy handling, and content/update integrity.

### Deserialization / dynamic loading

Inspect unsafe serializers, type-name activation, binary formats, reflection/dynamic loading, and plugin boundaries.

### CI / supply chain

Inspect floating action tags, workflow permissions, `pull_request_target`, secrets passed to third-party actions, release tokens, dependency locking, artifact verification, and signing.

## Reporting discipline

Do not call a design vulnerable merely because it uses HTTP, CRC, a public salt, or a low-popularity dependency. Explain the trust model and whether an attacker can exploit the specific path.
