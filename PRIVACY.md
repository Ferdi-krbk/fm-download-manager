# Privacy Policy for FM Download Manager & Browser Extension

**Last Updated:** October 2026

## 1. Overview
FM Download Manager and its companion Chromium browser extension are designed with a **privacy-first, local-only** philosophy. The software does not track, collect, sell, or transmit any personal information or browsing history.

## 2. Browser Extension Data Usage
The FM Download Manager browser extension requires certain permissions strictly to provide download management functionality:
* **`downloads` & `webRequest`:** Used solely to intercept user-initiated file downloads and media streams, redirecting them to the FM Download Manager desktop client.
* **`cookies`:** Used exclusively to forward authentication cookies for requested downloads to ensure restricted or logged-in downloads complete successfully. Cookies are sent **only to your local machine** (`127.0.0.1`) and are never sent to third-party servers.
* **`storage`:** Used to save your local extension settings (e.g., minimum file size intercept threshold, dark/light theme preferences).
* **`tabs` & `contextMenus`:** Used to provide right-click "Download with FM Download Manager" actions.

## 3. Communication
All communication between the browser extension and the desktop application occurs strictly over your **local loopback interface** (`http://127.0.0.1:48213`). No data leaves your computer.

## 4. Third-Party Services
FM Download Manager does not use analytics, telemetric trackers, ads, or external data harvesting services.

## 5. Contact & Open Source
FM Download Manager is an open-source project. You can inspect the complete source code on GitHub:
https://github.com/Ferdi-krbk/fm-download-manager
