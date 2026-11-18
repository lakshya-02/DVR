# DVR security policy

DVR’s Quest app plays personal media from Google Drive through the [Drive companion](DriveService/README.md). The companion holds the Google OAuth grant. The headset holds a separate device session credential.

## Drive access

- Use HTTPS for regular Drive playback. The USB pilot uses HTTP on Android loopback, which local processes share. Keep it in explicitly selected development builds on a trusted headset and PC.
- Authorize each account, folder, search, and media request against the current device session. Bind pagination to that session and the full query. Obtain the displayed account identity from Google using the same grant and let Drive enforce access to items, including shortcut targets.
- Keep the Google grant read-only. Encrypt refresh tokens at rest, hash device credentials on the service, and encrypt saved headset credentials with Android Keystore. Stop playback and clear private results when the session is invalidated. Block access while revocation is pending.

## Builds and private data

- Keep cleartext pilot support and intent-driven diagnostics out of distributable APKs. Use the bundled film for capture checks and keep private media and room imagery out of builds and shared artifacts.
- Keep OAuth credentials, storage keys, databases, signing files and local settings out of Git. Limit access to both the database and its encryption key, including backups. A directory containing both must be private to the service operator.
- Keep pair codes, authorization headers, account identity, private names, file IDs and resource keys out of logs, including proxy access logs and media errors. Serve private responses with `Cache-Control: no-store`.

Use mocked Google endpoints for routine security tests. Record real-device checks with the build hash and generic outcomes. Report issues privately to the project maintainer using source references and redacted evidence.
