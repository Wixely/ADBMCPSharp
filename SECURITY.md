# Security policy

## Reporting a vulnerability

Do not disclose suspected vulnerabilities through a public issue. Use GitHub's private vulnerability-reporting feature after the repository is published. Until then, contact the repository owner through an established private channel without including live ADB keys, device selectors, addresses, logs, or device output in an initial report.

## Deployment expectations

ADBMCPSharp controls a high-privilege ADB channel. Keep it on a trusted host and network, bind to loopback where possible, terminate TLS in a trusted reverse proxy or private overlay before any network exposure, use a high-entropy API key, keep all control gates disabled until needed, and protect ADB keys and configuration with operating-system permissions.

The project does not claim to make ADB safe across an untrusted network. A remote ADB server should itself be confined to a trusted network path and access-controlled outside this service.
