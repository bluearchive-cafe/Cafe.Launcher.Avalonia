# Security Audit

## Start With the Threat Model

State the relevant trust boundaries: remote metadata, local user input, update servers, filesystem, privileged installer paths, CI/release credentials, plugins/extensions, or multi-user data.

## Inspect

Choose checks from the actual stack and exposed boundaries. For a Web/API repository,
include the identity, session, input, and business-logic paths below; for a local library
or desktop tool, inspect only boundaries it actually exposes. These are investigation
prompts, not a requirement to report a finding in every category.

### Web/API identity, authorization, and sessions (when applicable)

- Trace authentication, recovery and token validation through the server-side path;
  check expiry, issuer/audience where relevant, and revocation/logout behavior.
- Verify object-, action-, and tenant-level authorization on every relevant access path.
  Include alternate endpoints, bulk operations, background jobs, and file downloads.
  A valid login or a hidden UI control does not establish authorization to the object.
- Review session fixation/rotation, cookie scope and Secure/HttpOnly/SameSite behavior,
  token storage, and sensitive browser actions. Evaluate CSRF against ambient credentials
  and actual cross-site request behavior; a stateless bearer-only API does not have the
  same exposure as an automatically authenticated cookie endpoint.

### Web/API input and output (when applicable)

- Follow attacker-controlled input into SQL/NoSQL queries, templates, and other interpreters.
  Check parameter binding and context-specific handling; string interpolation elsewhere
  is not proof of injection in a parameterized query.
- Trace reflected/stored data into HTML, DOM, script, and URL contexts for XSS, considering
  framework escaping, sanitization, and escape hatches. Validation alone is not output encoding.
- Evaluate CORS, content types, redirects, uploads, and exposed error detail in the actual
  browser/server trust model rather than treating one header as a complete defense.

### Business logic and resource abuse (when applicable)

For valuable state transitions, inspect ownership and allowed transitions, replay and
idempotency, concurrency, and abuse limits. Prioritize concrete consequences such as
duplicate charges, repeated redemption, unauthorized export, or unbounded expensive work.
Reproduce using local fixtures or authorized test environments; production probing is
not implied by a repository audit.

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

## References

Use the versioned [OWASP WSTG Web security domains](https://wstg.owasp.org/v4.2/4-Web_Application_Security_Testing/)
to select deeper guidance for identity, authentication, authorization, sessions, input,
business logic, and client-side behavior. Consult the actual framework's official
documentation for API-specific remedies. WSTG is a coverage aid, not proof that an
issue exists or that every category applies to this repository.
