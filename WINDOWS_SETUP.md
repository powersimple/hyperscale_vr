# Windows setup, step by step

From an empty Windows machine to Hyperscale VR running on a Meta Quest.

## 0 - Before you start (once)

0.1 GitHub Desktop
  a. Install GitHub Desktop for Windows (desktop.github.com).
  b. Open it, choose File > Options > Accounts, and sign in to GitHub as powersimple.

0.2 Unity with Android support
  a. Open Unity Hub and choose Installs.
  b. Use Unity 6.6 (6000.6.3f1), the editor already installed.
  c. On that editor, click the gear > Add modules.
  d. Tick Android Build Support, and under it OpenJDK and Android SDK & NDK Tools. Click Install. Without these, Unity cannot build an APK.

0.3 The headset in developer mode
  a. On your phone, open the Meta Horizon app, choose Devices, select the headset, then Headset settings > Developer mode, and turn it on. (Meta may ask you to create a free developer organization first.)
  b. Restart the headset.

0.4 The Cesium ion token
  a. Sign in at ion.cesium.com, open Access Tokens, and copy the token you use for the web atlas (or the Default token).
  b. Keep it to paste in step 6.

## 1 - Bring the repository to Windows

1.1 In GitHub Desktop, choose File > Clone Repository.
1.2 Click the URL tab and paste: https://github.com/powersimple/hyperscale_vr.git
1.3 Under Local path, choose the folder where you keep Unity projects. GitHub Desktop adds a hyperscale_vr folder inside it.
1.4 Click Clone.
1.5 Check: the new hyperscale_vr folder holds Assets, Packages, README.md, WINDOWS_SETUP.md, and setup_cesium.ps1.

## 2 - Make a Unity project from the VR template (this gives the project its XR rig and settings)

2.1 In Unity Hub, choose Projects > New project.
2.2 At the top, pick Unity 6000.6.3f1.
2.3 Choose the template named VR. If it shows a download button, download it first.
2.4 Project name: hyperscale_vr_template. Location: the same folder as in step 1.3.
2.5 Click Create project and wait until the Unity editor is fully open.
2.6 Close Unity (File > Exit). You will not use this project again; it only supplies files.

## 3 - Copy the template's files into the repository

3.1 In File Explorer, open the hyperscale_vr_template folder.
3.2 Select everything except these four folders: Library, Logs, Temp, UserSettings.
3.3 Copy (Ctrl+C).
3.4 Open the hyperscale_vr folder and paste (Ctrl+V).
3.5 If Windows asks to merge a folder, choose Yes. If it asks to replace a file, choose Skip these files, so the repository's own files stay.

## 4 - Add Cesium for Unity

4.1 In the hyperscale_vr folder, right-click setup_cesium.ps1 and choose Run with PowerShell.
4.2 If it asks about the execution policy, type Y and press Enter.
4.3 It prints "Cesium for Unity added." and closes.
4.4 If it does not run: in the folder, Shift + right-click on empty space, choose Open PowerShell window here, and type:
    powershell -ExecutionPolicy Bypass -File .\setup_cesium.ps1

## 5 - Open the project in Unity

5.1 In Unity Hub, choose Projects > Add (the arrow next to it) > Add project from disk.
5.2 Select the hyperscale_vr folder and click Add.
5.3 Click the project to open it with Unity 6000.6.3f1.
5.4 The first open takes several minutes: Unity downloads Cesium for Unity and imports everything.
5.5 If Unity asks to restart for the new Input System, choose Yes.
5.6 If Unity offers Safe Mode because of script errors, choose Ignore, open Window > General > Console, and send the red errors to Claude.

## 6 - Finish the setup inside Unity

6.1 TextMeshPro
  a. Window > TextMeshPro > Import TMP Essential Resources.
  b. In the window that opens, click Import.

6.2 The token
  a. Atlas VR > Set the Cesium ion token.
  b. Paste the token from step 0.4 and click Save. It stays on this machine and never goes into git.

6.3 The scene
  a. Atlas VR > 2. Create the scene.
  b. If Unity asks to save the current scene, choose Don't Save.
  c. The Atlas VR scene opens with the XR rig and the Atlas VR object.

6.4 The Quest settings
  a. Atlas VR > 3. Configure for Meta Quest.
  b. Unity switches to Android, which takes a few minutes. A window lists every setting it made; click OK.
  c. Open Edit > Project Settings > XR Plug-in Management. On the Android tab (the robot icon), OpenXR must be ticked. Under it, Meta Quest Support must be ticked. Tick them if not.

6.5 The check
  a. Atlas VR > 4. Check the setup.
  b. "Ready to build" means go on. Anything under "Needs attention": fix it, or send the list to Claude.

## 7 - Build and run on the headset

7.1 Connect the headset to the PC with a USB-C cable.
7.2 Put the headset on. When it asks to allow USB debugging, tick Always allow from this computer and choose Allow.
7.3 In Unity: Atlas VR > 6. Build and run on the headset.
7.4 The first build takes 10 to 20 minutes. Later builds are faster.
7.5 When it finishes, the app starts in the headset. Afterwards it is in the headset's Library, under Unknown Sources.
7.6 The APK file is also saved in the project's Builds folder. Any other Quest in developer mode can install it with:
    adb install -r <the apk file>
7.7 If Unity cannot see the headset: unplug and replug, accept the USB prompt again, and if needed install the Meta Quest ADB driver from Meta's developer site.

## 8 - Send the project settings back to git (once)

8.1 Open GitHub Desktop. It lists the files the template added: Packages, ProjectSettings, Assets/Samples, Assets/Settings, and the scene.
8.2 Check that no Library or Builds files are listed (the .gitignore keeps them out).
8.3 Summary: Unity project from the VR template. Click Commit to main.
8.4 Click Push origin.

## Every update after that

U.1 In GitHub Desktop: Fetch origin, then Pull origin.
U.2 Switch to Unity and wait for it to finish importing.
U.3 Atlas VR > 6. Build and run on the headset.
