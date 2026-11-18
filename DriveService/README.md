# Personal Google Drive setup

The Personal disc uses a companion service for Google sign-in, the Drive video list and video playback. You can try it with Quest connected by USB to your Windows PC or run a hosted HTTPS service. Google secrets and the database encryption key stay on the computer running the service.

## Try the wired pilot on Windows

1. Complete the Google Cloud setup below. For the Web application's exact authorized redirect URI use `http://127.0.0.1:48180/oauth/callback`. Download that client's JSON file from Google Auth Platform.
2. Connect one Quest to the PC by USB. Enable developer mode and approve USB debugging in the headset. Install Node.js 22.13 or later and Android SDK platform-tools, or use the ADB included with this project's Unity Editor.
3. From the repository root, run the command below with the path to the downloaded JSON. The script stores the credentials and an encryption key in the local ignored `.env.pilot`, sets up ADB reverse and starts the companion. Leave the PowerShell window open during playback. Subsequent runs can omit `-OAuthJson`.

   ```powershell
   .\DriveService\Start-WiredPilot.ps1 -OAuthJson 'C:\path\to\client_secret.json'
   ```

4. In the development APK, insert the Personal disc and press **Connect Google Drive**. Open `http://127.0.0.1:48180/pair` in the browser on that same PC, enter the headset code and continue with Google. When approval finishes, return to the headset to select a video.

A development Android build defaults to this wired address when it has no origin file. Keep the USB cable connected while browsing and playing. The service listens only on `127.0.0.1:48180`, and its `/health` endpoint reports `{"ok":true,"mode":"wired-pilot"}` once it has started. Sign in to Google to confirm your account's access. Running the pilot does not require a public domain, hosting account or tunnel.

## Configure Google Cloud

1. Enable the Google Drive API in a Google Cloud project.
2. In Google Auth Platform, complete Branding and Audience. For a personal External app in Testing, add the Google account you will use as a test user. An Internal app is available only to eligible Google Workspace projects and users in that organization.
3. Add `https://www.googleapis.com/auth/drive.readonly` under Data Access. This restricted scope lets DVR list videos across the user's Drive. Google can require OAuth verification and a security assessment before distributing an External app. Testing grants for this scope expire after seven days. [Google OAuth verification guidance](https://developers.google.com/identity/protocols/oauth2/production-readiness/restricted-scope-verification) · [Google refresh token lifetimes](https://developers.google.com/identity/protocols/oauth2#expiration)
4. Create a Web application OAuth client. Add the wired pilot redirect URI above or `https://dvr.example.com/oauth/callback` with your actual hosted service hostname. Save the client ID and secret for the service setup.

## Start the hosted service

Use Node.js 22.13 or later and keep a single service process on a host with a persistent disk. From `DriveService/` run `npm ci`, then copy `.env.example` to `.env`. Set the real `PUBLIC_ORIGIN`, `GOOGLE_CLIENT_ID` and `GOOGLE_CLIENT_SECRET`. Generate a storage key with `node -p "require('node:crypto').randomBytes(32).toString('base64')"` and set `STORAGE_KEY` to that output. Restrict the file and database to the service's OS user. Run `npm start` under the host's process supervisor with this directory as its working directory. Preserve the SQLite data directory and encryption key across restarts and backups.

Point DNS to the host and serve the public origin over trusted TLS. For Caddy on the same host, add this site block with your hostname.

```caddyfile
dvr.example.com {
    reverse_proxy 127.0.0.1:8080 {
        header_up X-Real-IP {remote_host}
    }
}
```

Keep `HOST=127.0.0.1` and `TRUST_PROXY=1` for that setup. The proxy must overwrite `X-Real-IP`, forward `Authorization` and `Range`, and stream video responses. Leave request and response caching and logging disabled for this origin, or redact authorization headers, query strings and response bodies. Confirm `https://dvr.example.com/health` returns `{"ok":true}`.

## Enable the hosted service on Quest

Before building DVR, create `Unity/Assets/Resources/DvrDriveSettings.json` containing the same HTTPS origin:

```json
{"origin":"https://dvr.example.com"}
```

Build and sideload DVR, then insert the Personal disc and select **Connect Google Drive**. Scan the displayed QR or open the displayed URL on a phone and enter the code. Verify the code and continue with Google. After consenting, return to the headset and choose a video. [Build instructions](../AGENTS.md)

Use Previous and Next to page through results and Search to enter a filename prefix. The list includes My Drive, items shared with your account and files from Shared Drives you can access. A directly shared file can also appear in its Shared Drive. A file must allow downloads and use a codec supported by the headset. DVR streams the original file and does not transcode it.

Open the Drive settings on the Personal panel and choose **Disconnect Google Drive** to stop playback, revoke the Google grant and remove the local device session. Google's revocation can affect other device sessions connected through the same OAuth client and Google account. If Google cannot process the revocation immediately, the service blocks playback and offers a retry. You can also remove DVR from [your Google Account connections](https://myaccount.google.com/connections).

## Check locally

Run `npm ci` and `npm test` from `DriveService/`. The tests use mocked Google endpoints and a temporary database. For the device acceptance check, pair a test account with one private H.264/AAC `.mp4` in My Drive and a second video shared with that account. Search for both, play and seek, restart DVR and confirm the pairing survived, then disconnect and check that playback requires a new pairing.
