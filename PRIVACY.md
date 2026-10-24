# Privacy Policy for FM Download Manager & Browser Extension

**Last Updated:** October 2026

## 1. Overview
FM Download Manager stores download history and settings locally. It does not use advertising or analytics trackers. The companion Chromium extension observes download and media requests to provide browser integration; the selected download's authentication information is passed to the local desktop app.

## 2. Browser Extension Data Usage
The FM Download Manager browser extension requires certain permissions to provide download management functionality:
* **`downloads` & `webRequest`:** Used solely to intercept user-initiated file downloads and media streams, redirecting them to the FM Download Manager desktop client.
* **`cookies`:** Forwards authentication cookies to the local desktop application for requested downloads. The application sends them to the matching download or subtitle origin, respecting domain, path, expiry and HTTPS restrictions. Pending download credentials are encrypted for the current Windows user using DPAPI; completed and cancelled jobs omit them from saved history.
* **`storage`:** Used to save your local extension settings (e.g., minimum file size intercept threshold, dark/light theme preferences).
* **`tabs` & `contextMenus`:** Used to provide right-click "Download with FM Download Manager" actions.

## 3. Communication
Communication between the browser extension and the desktop application uses the **local loopback interface** (`http://127.0.0.1:48213`). Download and subtitle requests contact the selected remote servers. Torrent downloads contact peers and trackers. Update checks contact GitHub. These requests expose ordinary connection metadata such as your IP address to those services.

Local download history contains URLs and filenames, and a recovery copy is stored beside it. URLs can themselves contain temporary access tokens; review history before sharing it. Pairing credentials are not exposed as web-accessible extension resources.

## 4. Third-Party Services
FM Download Manager does not use analytics, telemetric trackers, ads, or external data harvesting services.

## 5. Contact & Open Source
FM Download Manager is an open-source project. You can inspect the complete source code on GitHub:
https://github.com/Ferdi-krbk/fm-download-manager
