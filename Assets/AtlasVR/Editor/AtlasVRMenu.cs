// The Public Hyperscale menu: pull the package from the Mac, build the scene, configure the project
// for Meta Quest, check the setup, and build the APK (or build and run on the headset).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace AtlasVR.EditorTools
{
    public static class AtlasVRMenu
    {
        const string ServerKey = "AtlasVR.Server";
        const string DefaultServer = "https://obi-wan-v:3002";
        const string ScenePath = "Assets/AtlasVR/Scenes/PublicHyperscale.unity";
        const string PackageDir = "Assets/StreamingAssets/AtlasPackage";
        const string LocalConfigPath = "Assets/AtlasVR/Resources/AtlasVRLocal.json";
        const string AppId = "org.academyimmersive.publichyperscale";

        public static string Server
        {
            get { return EditorPrefs.GetString(ServerKey, DefaultServer).TrimEnd('/'); }
            set { EditorPrefs.SetString(ServerKey, value.TrimEnd('/')); }
        }

        // ------------------------------------------------------------ package
        [MenuItem("Public Hyperscale/1. Pull package from the Mac", priority = 1)]
        public static void Pull()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Public Hyperscale", "Asking the Mac for the newest package", 0f);
                string latest = GetText(Server + "/vr/packages/latest.json");
                string name = JsonUtility.FromJson<Latest>(latest).package;
                if (string.IsNullOrEmpty(name)) throw new Exception("The Mac has no package yet. On the Mac, run the export script.");
                string baseUrl = Server + "/vr/packages/" + name + "/";
                string manifestText = GetText(baseUrl + "manifest.json");
                var files = ManifestFiles(manifestText);

                string tmp = PackageDir + ".incoming";
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                File.WriteAllText(Path.Combine(tmp, "manifest.json"), manifestText);
                for (int i = 0; i < files.Count; i++)
                {
                    EditorUtility.DisplayProgressBar("Public Hyperscale", "Downloading " + files[i], (i + 1f) / (files.Count + 1f));
                    byte[] data = GetBytes(baseUrl + files[i]);
                    string dst = Path.Combine(tmp, files[i]);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.WriteAllBytes(dst, data);
                }
                if (Directory.Exists(PackageDir)) Directory.Delete(PackageDir, true);
                Directory.Move(tmp, PackageDir);

                // The Cesium ion token the web atlas uses, for this machine's builds only.
                string token = "";
                try { token = JsonUtility.FromJson<Config>(GetText(Server + "/config")).cesiumIonToken ?? ""; }
                catch (Exception e) { Debug.LogWarning("[Public Hyperscale] Could not read the ion token from the Mac: " + e.Message); }
                Directory.CreateDirectory(Path.GetDirectoryName(LocalConfigPath));
                var cfg = new LocalConfig { ionToken = token, packageName = name, pulledFrom = Server };
                File.WriteAllText(LocalConfigPath, JsonUtility.ToJson(cfg, true));
                AssetDatabase.Refresh();
                Debug.Log("[Public Hyperscale] Package " + name + " pulled (" + files.Count + " files)" + (token.Length > 0 ? ", ion token set." : ", no ion token."));
                EditorUtility.DisplayDialog("Public Hyperscale", "Package " + name + " is in the project." + (token.Length > 0 ? "" : "\n\nThe Mac did not return a Cesium ion token; the globe will be empty until one is set in Assets/AtlasVR/Resources/AtlasVRLocal.json."), "OK");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Public Hyperscale", "Could not pull the package from " + Server + ".\n\n" + e.Message + "\n\nIs the atlas server running on the Mac (npm run dev), and is the address right (Public Hyperscale > Set the Mac's address)?", "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
        }

        [MenuItem("Public Hyperscale/Import package from a folder…", priority = 2)]
        public static void ImportFolder()
        {
            string src = EditorUtility.OpenFolderPanel("Choose a package folder (it holds manifest.json)", "", "");
            if (string.IsNullOrEmpty(src)) return;
            if (!File.Exists(Path.Combine(src, "manifest.json"))) { EditorUtility.DisplayDialog("Public Hyperscale", "That folder has no manifest.json.", "OK"); return; }
            if (Directory.Exists(PackageDir)) Directory.Delete(PackageDir, true);
            CopyDir(src, PackageDir);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Public Hyperscale", "Package imported.", "OK");
        }

        [MenuItem("Public Hyperscale/Set the Cesium ion token…", priority = 4)]
        public static void SetToken() { TokenWindow.Open(); }

        public static void SaveToken(string token)
        {
            var cfg = File.Exists(LocalConfigPath) ? JsonUtility.FromJson<LocalConfig>(File.ReadAllText(LocalConfigPath)) : new LocalConfig();
            if (cfg == null) cfg = new LocalConfig();
            cfg.ionToken = (token ?? "").Trim();
            Directory.CreateDirectory(Path.GetDirectoryName(LocalConfigPath));
            File.WriteAllText(LocalConfigPath, JsonUtility.ToJson(cfg, true));
            AssetDatabase.Refresh();
            Debug.Log("[Public Hyperscale] Cesium ion token saved on this machine (kept out of git).");
        }

        public static string CurrentToken()
        {
            if (!File.Exists(LocalConfigPath)) return "";
            var cfg = JsonUtility.FromJson<LocalConfig>(File.ReadAllText(LocalConfigPath));
            return cfg != null ? cfg.ionToken ?? "" : "";
        }

        [MenuItem("Public Hyperscale/Set the Mac's address…", priority = 3)]
        public static void SetServer() { ServerWindow.Open(); }

        // ------------------------------------------------------------ scene
        [MenuItem("Public Hyperscale/2. Create the scene", priority = 20)]
        public static void CreateScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var rigPrefab = FindPrefab("XR Origin (XR Rig)") ?? FindPrefab("XR Origin");
            if (rigPrefab != null)
            {
                foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)) UnityEngine.Object.DestroyImmediate(cam.gameObject);
                var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab, scene);
                rig.transform.position = Vector3.zero;
            }
            else Debug.LogWarning("[Public Hyperscale] No XR Origin prefab found (the VR template's Starter Assets). The scene will use a desktop camera.");
            var app = new GameObject("Public Hyperscale");
            app.AddComponent<AtlasVRApp>();
            foreach (var l in UnityEngine.Object.FindObjectsByType<Light>())
            {
                if (l.type != LightType.Directional) continue;
                l.shadows = LightShadows.None;
                l.intensity = 1.2f;
                l.transform.rotation = Quaternion.Euler(40f, -30f, 0f);
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var list = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            list.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
            Debug.Log("[Public Hyperscale] Scene saved and set first in the build.");
        }

        // ------------------------------------------------------------ configure
        [MenuItem("Public Hyperscale/3. Configure for Meta Quest", priority = 21)]
        public static void ConfigureQuest()
        {
            var log = new StringBuilder();
            var android = NamedBuildTarget.Android;
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                log.AppendLine("Switched the build target to Android.");
            }
            PlayerSettings.companyName = "Academy of Immersive Arts and Sciences";
            PlayerSettings.productName = "Public Hyperscale";
            PlayerSettings.SetApplicationIdentifier(android, AppId);
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.forceInternetPermission = true;       // the globe streams from Cesium ion
            PlayerSettings.Android.blitType = AndroidBlitType.Never;
            PlayerSettings.Android.optimizedFramePacing = false;        // the XR runtime paces frames
            PlayerSettings.SetManagedStrippingLevel(android, ManagedStrippingLevel.Low);
            PlayerSettings.stripEngineCode = true;
            PlayerSettings.MTRendering = true;
            PlayerSettings.SplashScreen.show = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            log.AppendLine("Player: IL2CPP, ARM64, Android 12L (API 32) minimum, Vulkan only, linear color, internet permission forced, ASTC, no splash, low stripping.");

            // Quality: no realtime shadows; the Cesium tiles and markers are unlit or lightly lit.
            QualitySettings.shadows = ShadowQuality.Disable;
            QualitySettings.vSyncCount = 0;
            log.AppendLine("Quality: shadows off, vSync off (the headset paces frames).");

            // Every URP asset in the project: 4x MSAA, no HDR, no depth or opaque copies, no shadows.
            foreach (var guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                var so = new SerializedObject(asset);
                SetInt(so, "m_MSAA", 4); SetBool(so, "m_SupportsHDR", false); SetFloat(so, "m_RenderScale", 1f);
                SetBool(so, "m_RequireDepthTexture", false); SetBool(so, "m_RequireOpaqueTexture", false);
                SetBool(so, "m_MainLightShadowsSupported", false); SetBool(so, "m_AdditionalLightShadowsSupported", false);
                SetBool(so, "m_SoftShadowsSupported", false); SetBool(so, "m_UseSRPBatcher", true);
                so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("URP asset " + asset.name + ": MSAA 4x, HDR off, depth and opaque textures off, shadows off.");
            }

            // Keep GPU instancing variants: the markers' materials are made at runtime, so the default
            // "strip unused" would remove the variant they need from the build.
            var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (gs != null && gs.Length > 0)
            {
                var gso = new SerializedObject(gs[0]);
                SetInt(gso, "m_InstancingStripping", 2);
                gso.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("Graphics: instancing variants kept.");
            }

            log.Append(XrSetup.Configure());
            AssetDatabase.SaveAssets();
            Debug.Log("[Public Hyperscale] Configured for Meta Quest:\n" + log);
            EditorUtility.DisplayDialog("Public Hyperscale", log.ToString(), "OK");
        }

        // ------------------------------------------------------------ check
        [MenuItem("Public Hyperscale/4. Check the setup", priority = 22)]
        public static void Check()
        {
            var ok = new List<string>(); var bad = new List<string>();
            Action<bool, string, string> chk = (c, good, problem) => (c ? ok : bad).Add(c ? good : problem);
            string manifest = Path.Combine(PackageDir, "manifest.json");
            chk(File.Exists(manifest), "Package present: " + (File.Exists(manifest) ? JsonUtility.FromJson<Latest>(File.ReadAllText(manifest)).package : ""), "No package. Run Public Hyperscale > Pull package from the Mac.");
            var cfgText = File.Exists(LocalConfigPath) ? File.ReadAllText(LocalConfigPath) : "";
            chk(cfgText.Contains("\"ionToken\": \"") && !cfgText.Contains("\"ionToken\": \"\""), "Cesium ion token set.", "No Cesium ion token. Public Hyperscale > Set the Cesium ion token.");
            chk(EditorBuildSettings.scenes.Length > 0 && EditorBuildSettings.scenes[0].path == ScenePath, "The scene is first in the build.", "Run Public Hyperscale > Create the scene.");
            chk(Type.GetType("CesiumForUnity.Cesium3DTileset, CesiumForUnity") != null, "Cesium for Unity installed.", "Cesium for Unity is missing (see the README, step 2).");
            chk(AssetDatabase.FindAssets("t:TMP_Settings").Length > 0, "TextMeshPro essentials imported.", "Import TextMeshPro essentials (Window > TextMeshPro > Import TMP Essential Resources).");
            chk(EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android, "Build target is Android.", "Build target is not Android. Run Public Hyperscale > Configure for Meta Quest.");
            chk(PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP, "IL2CPP.", "Scripting backend is not IL2CPP.");
            chk(PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64, "ARM64 only.", "Target architectures should be ARM64 only.");
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            chk(apis.Length > 0 && apis[0] == GraphicsDeviceType.Vulkan, "Vulkan.", "Graphics API should be Vulkan.");
            chk(PlayerSettings.Android.forceInternetPermission, "Internet permission forced.", "Internet permission is not forced; the globe would stay empty.");
            var gsa = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            var inst = gsa != null && gsa.Length > 0 ? new SerializedObject(gsa[0]).FindProperty("m_InstancingStripping") : null;
            chk(inst != null && inst.intValue == 2, "Instancing variants kept.", "Instancing variants may be stripped (Project Settings > Graphics > Instancing Variants: Keep All).");
            foreach (var line in XrSetup.Report()) (line.StartsWith("!") ? bad : ok).Add(line.TrimStart('!'));
            string msg = (bad.Count == 0 ? "Ready to build.\n\n" : "Needs attention:\n- " + string.Join("\n- ", bad) + "\n\n") + "Fine:\n- " + string.Join("\n- ", ok);
            Debug.Log("[Public Hyperscale] " + msg);
            EditorUtility.DisplayDialog("Public Hyperscale", msg, "OK");
        }

        // ------------------------------------------------------------ build
        [MenuItem("Public Hyperscale/5. Build APK", priority = 40)]
        public static void BuildApk() { Build(false); }

        [MenuItem("Public Hyperscale/6. Build and run on the headset", priority = 41)]
        public static void BuildAndRun() { Build(true); }

        static void Build(bool run)
        {
            if (!File.Exists(Path.Combine(PackageDir, "manifest.json"))) { EditorUtility.DisplayDialog("Public Hyperscale", "Pull the package first.", "OK"); return; }
            if (EditorBuildSettings.scenes.Length == 0) CreateScene();
            Directory.CreateDirectory("Builds");
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
            string path = "Builds/PublicHyperscale_" + stamp + ".apk";
            var opts = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = run ? BuildOptions.AutoRunPlayer : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var sum = report.summary;
            string msg = sum.result == UnityEditor.Build.Reporting.BuildResult.Succeeded
                ? "Built " + path + " (" + (sum.totalSize / (1024 * 1024)) + " MB) in " + sum.totalTime.TotalMinutes.ToString("0.0") + " min." + (run ? "\nInstalled and started on the headset." : "\nSideload: adb install -r \"" + Path.GetFullPath(path) + "\"")
                : "The build did not succeed: " + sum.result + ". The Console has the errors.";
            Debug.Log("[Public Hyperscale] " + msg);
            EditorUtility.DisplayDialog("Public Hyperscale", msg, "OK");
        }

        // ------------------------------------------------------------ helpers
        [Serializable] class Latest { public string package; }
        [Serializable] class Config { public string cesiumIonToken; }

        static List<string> ManifestFiles(string manifest)
        {
            // manifest.files is a map of path -> {bytes, sha256}; JsonUtility cannot read maps, so list the keys.
            var files = new List<string>();
            int i = manifest.IndexOf("\"files\"", StringComparison.Ordinal);
            if (i < 0) return files;
            int depth = 0;
            for (int j = manifest.IndexOf('{', i); j < manifest.Length; j++)
            {
                char c = manifest[j];
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) break; }
                else if (c == '"' && depth == 1)
                {
                    int end = manifest.IndexOf('"', j + 1);
                    files.Add(manifest.Substring(j + 1, end - j - 1));
                    j = manifest.IndexOf('}', end); // skip this entry's object
                }
            }
            return files;
        }

        class TrustMacCertificate : CertificateHandler
        {
            // The Mac's dev server uses a self-signed certificate; accept it for that server only.
            protected override bool ValidateCertificate(byte[] certificateData) { return true; }
        }

        static UnityWebRequest Send(string url)
        {
            var req = UnityWebRequest.Get(url);
            req.timeout = 60;
            if (url.StartsWith(Server)) req.certificateHandler = new TrustMacCertificate();
            var op = req.SendWebRequest();
            while (!op.isDone) System.Threading.Thread.Sleep(20);
            if (req.result != UnityWebRequest.Result.Success) { string e = url + ": " + req.error; req.Dispose(); throw new Exception(e); }
            return req;
        }

        static string GetText(string url) { using (var r = Send(url)) return r.downloadHandler.text; }
        static byte[] GetBytes(string url) { using (var r = Send(url)) return r.downloadHandler.data; }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        static GameObject FindPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets("\"" + name + "\" t:Prefab"))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<GameObject>(p);
            }
            return null;
        }

        static void SetInt(SerializedObject so, string n, int v) { var p = so.FindProperty(n); if (p != null) p.intValue = v; }
        static void SetBool(SerializedObject so, string n, bool v) { var p = so.FindProperty(n); if (p != null) p.boolValue = v; }
        static void SetFloat(SerializedObject so, string n, float v) { var p = so.FindProperty(n); if (p != null) p.floatValue = v; }
    }

    public class TokenWindow : EditorWindow
    {
        string _value;
        public static void Open()
        {
            var w = GetWindow<TokenWindow>(true, "Cesium ion token", true);
            w._value = AtlasVRMenu.CurrentToken();
            w.minSize = new Vector2(520, 110);
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("Paste the Cesium ion access token (it stays on this machine):");
            _value = EditorGUILayout.TextField(_value);
            EditorGUILayout.Space();
            if (GUILayout.Button("Save")) { AtlasVRMenu.SaveToken(_value); Close(); }
        }
    }

    public class ServerWindow : EditorWindow
    {
        string _value;
        public static void Open()
        {
            var w = GetWindow<ServerWindow>(true, "The Mac's address", true);
            w._value = AtlasVRMenu.Server;
            w.minSize = new Vector2(420, 110);
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("The atlas server on the Mac (scheme, name, port):");
            _value = EditorGUILayout.TextField(_value);
            EditorGUILayout.Space();
            if (GUILayout.Button("Save")) { AtlasVRMenu.Server = _value; Close(); }
        }
    }

    /// OpenXR and XR Plug-in Management settings, reached by reflection so the menu compiles
    /// whatever versions the template brought. Every change is reported.
    public static class XrSetup
    {
        static Type T(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(name, false);
                if (t != null) return t;
            }
            return null;
        }

        static object OpenXRSettingsFor(BuildTargetGroup g)
        {
            var t = T("UnityEngine.XR.OpenXR.OpenXRSettings");
            var m = t != null ? t.GetMethod("GetSettingsForBuildTargetGroup", BindingFlags.Public | BindingFlags.Static) : null;
            return m != null ? m.Invoke(null, new object[] { g }) : null;
        }

        static List<object> Features(object settings)
        {
            // OpenXRSettings has GetFeatures() and GetFeatures<T>(); pick the plain one.
            var list = new List<object>();
            if (settings == null) return list;
            try
            {
                var m = settings.GetType().GetMethods().FirstOrDefault(x => x.Name == "GetFeatures" && !x.IsGenericMethodDefinition && x.GetParameters().Length == 0);
                var arr = m != null ? m.Invoke(settings, null) as Array : null;
                if (arr != null) foreach (var f in arr) list.Add(f);
            }
            catch (Exception e) { Debug.LogWarning("[Public Hyperscale] Could not read the OpenXR features: " + e.GetBaseException().Message); }
            return list;
        }

        static bool Set(object o, string member, object value)
        {
            if (o == null) return false;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = o.GetType().GetProperty(member, f);
            if (p != null && p.CanWrite) { p.SetValue(o, value is IConvertible && !p.PropertyType.IsEnum ? Convert.ChangeType(value, p.PropertyType) : value, null); return true; }
            var fi = o.GetType().GetField(member, f);
            if (fi != null) { fi.SetValue(o, value is IConvertible && !fi.FieldType.IsEnum ? Convert.ChangeType(value, fi.FieldType) : value); return true; }
            return false;
        }

        static object Get(object o, string member)
        {
            if (o == null) return null;
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var p = o.GetType().GetProperty(member, f);
            if (p != null) return p.GetValue(o, null);
            var fi = o.GetType().GetField(member, f);
            return fi != null ? fi.GetValue(o) : null;
        }

        public static string Configure()
        {
            var log = new StringBuilder();
            // OpenXR as the loader on Android and on Windows.
            var store = T("UnityEditor.XR.Management.Metadata.XRPackageMetadataStore");
            var perTarget = T("UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget");
            foreach (var g in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
            {
                try
                {
                    var gs = perTarget != null ? perTarget.GetMethod("XRGeneralSettingsForBuildTarget", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { g }) : null;
                    var mgr = Get(gs, "Manager") ?? Get(gs, "AssignedSettings");
                    var assign = store != null ? store.GetMethod("AssignLoader", BindingFlags.Public | BindingFlags.Static) : null;
                    if (mgr != null && assign != null) { assign.Invoke(null, new object[] { mgr, "UnityEngine.XR.OpenXR.OpenXRLoader", g }); log.AppendLine("OpenXR loader assigned for " + g + "."); }
                    else log.AppendLine("OpenXR loader for " + g + ": open Project Settings > XR Plug-in Management once and tick OpenXR on the " + (g == BuildTargetGroup.Android ? "Android" : "Windows") + " tab.");
                    if (gs != null) Set(gs, "InitManagerOnStart", true);
                }
                catch (Exception e) { log.AppendLine("OpenXR loader for " + g + " not set automatically (" + e.GetBaseException().Message + "); set it in Project Settings > XR Plug-in Management."); }
            }

            // Quest: the Meta Quest feature (internet permission kept), Touch controllers, hand tracking, multiview.
            var android = OpenXRSettingsFor(BuildTargetGroup.Android);
            if (android == null) { log.AppendLine("OpenXR settings for Android not found; is the OpenXR package installed?"); return log.ToString(); }
            foreach (var f in Features(android))
            {
                string n = f.GetType().Name;
                string ui = Get(f, "nameUi") as string ?? "";
                // Mixed reality: Meta OpenXR's session and camera (passthrough) features, for the room switch.
                // Meta's own (namespace ...Features.Meta), not the Android XR features of the same names.
                bool metaNs = f.GetType().Namespace == "UnityEngine.XR.OpenXR.Features.Meta";
                bool passthrough = metaNs && (n == "ARSessionFeature" || n == "ARCameraFeature");
                bool want = n == "MetaQuestFeature" || n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || n == "MetaQuestTouchProControllerProfile"
                         || n == "HandTracking" || n == "MetaHandTrackingAim" || n == "HandInteractionProfile" || n == "FoveatedRenderingFeature" || passthrough;
                if (want) { Set(f, "enabled", true); log.AppendLine("OpenXR feature on (Android): " + n + "."); }
                if (n == "MetaQuestFeature")
                {
                    if (Set(f, "forceRemoveInternetPermission", false)) log.AppendLine("Meta Quest feature keeps the internet permission.");
                    Set(f, "symmetricProjection", true);
                    Set(f, "optimizeBufferDiscards", true);
                    Set(f, "lateLatchingMode", true);
                    Set(f, "lateLatchingDebug", false);
                    Set(f, "spacewarpMotionVectorTextureFormat", 0);
                    EditorUtility.SetDirty((UnityEngine.Object)f);
                }
                else if (want) EditorUtility.SetDirty((UnityEngine.Object)f);
            }
            var rmType = T("UnityEngine.XR.OpenXR.OpenXRSettings+RenderMode");
            if (rmType != null && Set(android, "renderMode", Enum.Parse(rmType, "SinglePassInstanced"))) log.AppendLine("OpenXR render mode (Android): single pass (multiview).");
            Set(android, "optimizeBufferDiscards", true);
            Set(android, "symmetricProjection", true);
            if (android is UnityEngine.Object) EditorUtility.SetDirty((UnityEngine.Object)android);

            var pc = OpenXRSettingsFor(BuildTargetGroup.Standalone);
            foreach (var f in Features(pc))
            {
                string n = f.GetType().Name;
                if (n == "OculusTouchControllerProfile" || n == "MetaQuestTouchPlusControllerProfile" || n == "HandTracking" || n == "MetaHandTrackingAim") { Set(f, "enabled", true); EditorUtility.SetDirty((UnityEngine.Object)f); log.AppendLine("OpenXR feature on (Windows): " + n + "."); }
            }
            if (rmType != null && pc != null) Set(pc, "renderMode", Enum.Parse(rmType, "SinglePassInstanced"));
            return log.ToString();
        }

        public static IEnumerable<string> Report()
        {
            var android = OpenXRSettingsFor(BuildTargetGroup.Android);
            if (android == null) { yield return "!OpenXR is not set up for Android."; yield break; }
            bool meta = false, touch = false, internetRemoved = false;
            foreach (var f in Features(android))
            {
                string n = f.GetType().Name;
                bool on = Get(f, "enabled") is bool && (bool)Get(f, "enabled");
                if (n == "MetaQuestFeature") { meta = on; var r = Get(f, "forceRemoveInternetPermission"); internetRemoved = r is bool && (bool)r; }
                if (n.Contains("Touch") && on) touch = true;
            }
            yield return meta ? "Meta Quest feature on." : "!Meta Quest feature is off (Project Settings > XR Plug-in Management > OpenXR > Android).";
            yield return touch ? "Touch controller profile on." : "!No Touch controller profile enabled.";
            bool pass = false;
            foreach (var f in Features(android))
            {
                string n = f.GetType().Name, ui = Get(f, "nameUi") as string ?? "";
                if (n == "ARCameraFeature" && f.GetType().Namespace == "UnityEngine.XR.OpenXR.Features.Meta" && Get(f, "enabled") is bool && (bool)Get(f, "enabled")) pass = true;
            }
            yield return pass ? "Passthrough (mixed reality) feature on." : "!Passthrough feature is off: the room switch will say it is unavailable (run Configure for Meta Quest).";
            yield return internetRemoved ? "!The Meta Quest feature removes the internet permission; untick Force Remove Internet Permission." : "Internet permission kept by the Meta Quest feature.";
        }
    }
}
