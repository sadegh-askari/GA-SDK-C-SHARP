using System;
using System.Collections.Generic;
using GameAnalyticsSDK.Events;
using GameAnalyticsSDK.Net.Threading;
using GameAnalyticsSDK.Net.Logging;
using GameAnalyticsSDK.Net.State;
using GameAnalyticsSDK.Net.Validators;
using GameAnalyticsSDK.Net.Device;
using GameAnalyticsSDK.Net.Events;
using GameAnalyticsSDK.Net.Store;
using UnityEngine;
#if WINDOWS_UWP || WINDOWS_WSA
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using System.Threading.Tasks;
#else
using System.Threading;
#endif

#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

#if UNITY_IOS
using GameAnalyticsSDK.iOS;
#endif


namespace GameAnalyticsSDK.Net
{
    [RequireComponent(typeof(GA_SpecialEvents))]
    [ExecuteInEditMode]
    public class GameAnalytics : MonoBehaviour
    {
        public struct CustomConfig
        {
            public string Name;
            public string Value;
        }
        public static List<CustomConfig> CustomConfigs { get; private set; }

        public static void AddCustomConfig(string name, string value)
        {
            CustomConfigs ??= new List<CustomConfig>();

            CustomConfigs.Add(new CustomConfig
            {
                Name = name,
                Value = value
            });
        }

         #region public values

        private static GameAnalyticsSDK.Setup.Settings _settings;

        public static GameAnalyticsSDK.Setup.Settings SettingsGA
        {
            get
            {
                if (_settings == null)
                {
                    InitAPI();
                }
                return _settings;
            }
            private set { _settings = value; }
        }

        private static GameAnalytics _instance;

        #endregion

        private static bool _hasInitializeBeenCalled;

        #region unity derived methods

        #if UNITY_EDITOR
        void OnEnable()
        {
            EditorApplication.hierarchyWindowItemOnGUI += GameAnalytics.HierarchyWindowCallback;

            if (Application.isPlaying)
                _instance = this;
        }

        void OnDisable()
        {
            EditorApplication.hierarchyWindowItemOnGUI -= GameAnalytics.HierarchyWindowCallback;
        }
        #else
        void OnEnable()
        {
            Application.logMessageReceived += GA_Debug.HandleLog;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= GA_Debug.HandleLog;
        }
        #endif

        public void Awake()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_instance != null)
            {
                // only one system tracker allowed per scene
                Debug.LogWarning("Destroying duplicate GameAnalytics object - only one is allowed per scene!");
                Destroy(gameObject);
                return;
            }
            _instance = this;

            DontDestroyOnLoad(gameObject);

            _endThread = false;
            GADevice.Touch();
        }

        void OnDestroy()
        {
            if (!Application.isPlaying)
                return;

            if (_instance == this)
                _instance = null;
        }

#if (!UNITY_EDITOR && UNITY_WSA)
        [DllImport("GameAnalytics.UWP.dll")]
        private static extern void onQuit();
#endif

        void OnApplicationQuit()
        {
#if (!UNITY_EDITOR && !UNITY_IOS && !UNITY_ANDROID && !UNITY_TVOS && !UNITY_WEBGL && !UNITY_TIZEN && !UNITY_SWITCH && !UNITY_PS4 && !UNITY_XBOXONE)
#if (UNITY_WSA)
            onQuit();
#else
            GameAnalyticsSDK.Net.GameAnalytics.OnQuit();
# endif
#if UNITY_STANDALONE
            System.Threading.Thread.Sleep(1500);
#endif
#endif
        }

#endregion

        private static void InitAPI()
        {
            try
            {
                _settings = (GameAnalyticsSDK.Setup.Settings) Resources.Load("GameAnalytics/Settings", typeof(GameAnalyticsSDK.Setup.Settings));
                GameAnalyticsSDK.State.GAState.Init();

#if UNITY_EDITOR
                if (_settings == null)
                {
                    //If the settings asset doesn't exist, then create it. We require a resources folder
                    if (!Directory.Exists(Application.dataPath + "/Resources"))
                    {
                        Directory.CreateDirectory(Application.dataPath + "/Resources");
                    }
                    if (!Directory.Exists(Application.dataPath + "/Resources/GameAnalytics"))
                    {
                        Directory.CreateDirectory(Application.dataPath + "/Resources/GameAnalytics");
                        Debug.LogWarning("GameAnalytics: Resources/GameAnalytics folder is required to store settings. it was created ");
                    }

                    const string path = "Assets/Resources/GameAnalytics/Settings.asset";

                    if (File.Exists(path))
                    {
                        AssetDatabase.DeleteAsset(path);
                        AssetDatabase.Refresh();
                    }

                    var asset = ScriptableObject.CreateInstance<GameAnalyticsSDK.Setup.Settings>();
                    AssetDatabase.CreateAsset(asset, path);
                    AssetDatabase.Refresh();

                    AssetDatabase.SaveAssets();
                    Debug.LogWarning("GameAnalytics: Settings file didn't exist and was created");
                    Selection.activeObject = asset;

                    //save reference
                    _settings = asset;
                }
#endif
            }
            catch (System.Exception e)
            {
                Debug.Log("Error getting Settings in InitAPI: " + e.Message);
            }
        }

        private static void InternalInitialize()
        {
            if (!Application.isPlaying)
                return; // no need to setup anything else if we are in the editor and not playing

            if (SettingsGA.InfoLogBuild)
            {
                //GA_Setup.SetInfoLog(true);
                SetEnabledInfoLog(true);
            }

            if (SettingsGA.VerboseLogBuild)
            {
                //GA_Setup.SetVerboseLog(true);
                SetEnabledVerboseLog(true);
            }

            int platformIndex = GetPlatformIndex();

            //GA_Wrapper.SetUnitySdkVersion("unity " + GameAnalyticsSDK.Setup.Settings.VERSION);
            ConfigureSdkGameEngineVersion("unity " + GameAnalyticsSDK.Setup.Settings.VERSION);
            //GA_Wrapper.SetUnityEngineVersion("unity " + GetUnityVersion());
            ConfigureGameEngineVersion("unity " + GetUnityVersion());

            if (platformIndex >= 0)
            {
                if (GameAnalytics.SettingsGA.UsePlayerSettingsBuildNumber)
                {
                    for (int i = 0; i < GameAnalytics.SettingsGA.Platforms.Count; ++i)
                    {
                        if (GameAnalytics.SettingsGA.Platforms[i] == RuntimePlatform.Android || GameAnalytics.SettingsGA.Platforms[i] == RuntimePlatform.IPhonePlayer)
                        {
                            GameAnalytics.SettingsGA.Build[i] = Application.version;
                        }
                    }
                    if (GameAnalytics.SettingsGA.Platforms[platformIndex] == RuntimePlatform.Android || GameAnalytics.SettingsGA.Platforms[platformIndex] == RuntimePlatform.IPhonePlayer)
                    {
                        //GA_Wrapper.SetAutoDetectAppVersion(true);
                        ConfigureBuild(SettingsGA.Build[platformIndex]);
                    }
                    else
                    {
                        //GA_Wrapper.SetBuild(SettingsGA.Build[platformIndex]);
                        ConfigureBuild(SettingsGA.Build[platformIndex]);
                    }
                }
                else
                {
                    //GA_Wrapper.SetBuild(SettingsGA.Build[platformIndex]);
                    ConfigureBuild(SettingsGA.Build[platformIndex]);
                }
            }

            if (SettingsGA.CustomDimensions01.Count > 0)
            {
                //GA_Setup.SetAvailableCustomDimensions01(SettingsGA.CustomDimensions01);
                ConfigureAvailableCustomDimensions01(SettingsGA.CustomDimensions01.ToArray());
            }

            if (SettingsGA.CustomDimensions02.Count > 0)
            {
                //GA_Setup.SetAvailableCustomDimensions02(SettingsGA.CustomDimensions02);
                ConfigureAvailableCustomDimensions02(SettingsGA.CustomDimensions02.ToArray());
            }

            if (SettingsGA.CustomDimensions03.Count > 0)
            {
                //GA_Setup.SetAvailableCustomDimensions03(SettingsGA.CustomDimensions03);
                ConfigureAvailableCustomDimensions03(SettingsGA.CustomDimensions03.ToArray());
            }

            if (SettingsGA.ResourceItemTypes.Count > 0)
            {
                //GA_Setup.SetAvailableResourceItemTypes(SettingsGA.ResourceItemTypes);
                ConfigureAvailableResourceItemTypes(SettingsGA.ResourceItemTypes.ToArray());
            }

            if (SettingsGA.ResourceCurrencies.Count > 0)
            {
                //GA_Setup.SetAvailableResourceCurrencies(SettingsGA.ResourceCurrencies);
                ConfigureAvailableResourceCurrencies(SettingsGA.ResourceCurrencies.ToArray());
            }

            if (SettingsGA.UseManualSessionHandling)
            {
                SetEnabledManualSessionHandling(true);
            }
        }

        public static void Initialize()
        {

            InternalInitialize();
            int platformIndex = GetPlatformIndex();

            #if UNITY_EDITOR
            platformIndex = 0;
#endif

            if (platformIndex >= 0)
            {
                //GA_Wrapper.Initialize (SettingsGA.GetGameKey (platformIndex), SettingsGA.GetSecretKey (platformIndex));
                Initialize(SettingsGA.GetGameKey(platformIndex), SettingsGA.GetSecretKey(platformIndex));

                GameAnalytics._hasInitializeBeenCalled = true;
            }
            else
            {
                GameAnalytics._hasInitializeBeenCalled = true;
                Debug.LogWarning("GameAnalytics: Unsupported platform (events will not be sent in editor; or missing platform in settings): " + Application.platform);
            }
        }

        //****************************************************
        //****************************************************
        // ----------------------- IOS 14+ APP TRACKING TRANSPARENCY ---------------------- //
        public static void RequestTrackingAuthorization(IGameAnalyticsATTListener listener)
        {
#if UNITY_IOS
            GameAnalyticsATTClient.Instance.RequestTrackingAuthorization(listener);
#endif
        }

        private static string GetUnityVersion()
        {
            string unityVersion = "";
            string[] splitUnityVersion = Application.unityVersion.Split('.');
            for (int i = 0; i < splitUnityVersion.Length; i++)
            {
                int result;
                if (int.TryParse(splitUnityVersion[i], out result))
                {
                    if (i == 0)
                        unityVersion = splitUnityVersion[i];
                    else
                        unityVersion += "." + splitUnityVersion[i];
                }
                else
                {
                    string[] regexVersion = System.Text.RegularExpressions.Regex.Split(splitUnityVersion[i], "[^\\d]+");
                    if (regexVersion.Length > 0 && int.TryParse(regexVersion[0], out result))
                    {
                        unityVersion += "." + regexVersion[0];
                    }
                }
            }

            return unityVersion;
        }

        private static int GetPlatformIndex()
        {
            int result = -1;

            RuntimePlatform platform = Application.platform;

            if (platform == RuntimePlatform.IPhonePlayer)
            {
                if (!SettingsGA.Platforms.Contains(platform))
                {
                    result = SettingsGA.Platforms.IndexOf(RuntimePlatform.tvOS);
                }
                else
                {
                    result = SettingsGA.Platforms.IndexOf(platform);
                }
            }
            else if (platform == RuntimePlatform.tvOS)
            {
                if (!SettingsGA.Platforms.Contains(platform))
                {
                    result = SettingsGA.Platforms.IndexOf(RuntimePlatform.IPhonePlayer);
                }
                else
                {
                    result = SettingsGA.Platforms.IndexOf(platform);
                }
            }
            // HACK: To also check for RuntimePlatform.MetroPlayerARM, RuntimePlatform.MetroPlayerX64 and RuntimePlatform.MetroPlayerX86 which are deprecated but have same value as the WSA enums
            else if (platform == RuntimePlatform.WSAPlayerARM || platform == RuntimePlatform.WSAPlayerX64 || platform == RuntimePlatform.WSAPlayerX86 ||
                     ((int) platform == (int) RuntimePlatform.WSAPlayerARM) || ((int) platform == (int) RuntimePlatform.WSAPlayerX64) || ((int) platform == (int) RuntimePlatform.WSAPlayerX86))
            {
                result = SettingsGA.Platforms.IndexOf(RuntimePlatform.WSAPlayerARM);
            }
            else
            {
                result = SettingsGA.Platforms.IndexOf(platform);
            }

            return result;
        }

#if UNITY_EDITOR

        /// <summary>
        /// Dynamic search for a file.
        /// </summary>
        /// <returns>Returns the Unity path to a specified file.</returns>
        /// <param name="">File name including extension e.g. image.png</param>
        public static string WhereIs(string _file, string _type)
        {
#if UNITY_SAMSUNGTV
            return "";
#else
            string[] guids = AssetDatabase.FindAssets("t:" + _type);
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.EndsWith(_file))
                {
                    return p;
                }
            }
            return "";
#endif
        }

        public static void HierarchyWindowCallback(int instanceID, Rect selectionRect)
        {
            GameObject go = (GameObject) EditorUtility.InstanceIDToObject(instanceID);
            if (go != null && go.GetComponent<GameAnalytics>() != null)
            {
                float addX = 0;
                if (go.GetComponent("PlayMakerFSM") != null)
                    addX = selectionRect.height + 2;

                if (GameAnalytics.SettingsGA.Logo == null)
                {
                    GameAnalytics.SettingsGA.Logo = (Texture2D) AssetDatabase.LoadAssetAtPath(WhereIs("gaLogo.png", "Texture2D"), typeof(Texture2D));
                }

                Graphics.DrawTexture(new Rect(GUILayoutUtility.GetLastRect().width - selectionRect.height - 5 - addX, selectionRect.y, selectionRect.height, selectionRect.height), GameAnalytics.SettingsGA.Logo);
            }
        }

#endif
        /// <summary>
        /// Sets the build for all platforms.
        /// </summary>
        /// <param name="build">Build.</param>
        public static void SetBuildAllPlatforms(string build)
        {
            for (int i = 0; i < GameAnalytics.SettingsGA.Build.Count; i++)
            {
                GameAnalytics.SettingsGA.Build[i] = build;
            }
        }

        //****************************************************
        //****************************************************




        private static bool _endThread;

        static GameAnalytics()
        {
            _endThread = false;
            GADevice.Touch();
        }

#if !UNITY && !MONO
        public static event Action<string, EGALoggerMessageType> OnMessageLogged;

        internal static void MessageLogged(string message, EGALoggerMessageType type)
        {
            OnMessageLogged?.Invoke(message, type);
        }
#endif


        #region CONFIGURE

        public static void ConfigureAvailableCustomDimensions01(params string[] customDimensions)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions01", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions01 = customDimensions;
            });
        }

        public static void ConfigureAvailableCustomDimensions02(params string[] customDimensions)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions02", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions02 = customDimensions;
            });
        }

        public static void ConfigureAvailableCustomDimensions03(params string[] customDimensions)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableCustomDimensions03", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available custom dimensions must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableCustomDimensions03 = customDimensions;
            });
        }

        public static void ConfigureAvailableResourceCurrencies(params string[] resourceCurrencies)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableResourceCurrencies", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available resource currencies must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableResourceCurrencies = resourceCurrencies;
            });
        }

        public static void ConfigureAvailableResourceItemTypes(params string[] resourceItemTypes)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureAvailableResourceItemTypes", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Available resource item types must be set before SDK is initialized");
                    return;
                }
                GAState.AvailableResourceItemTypes = resourceItemTypes;
            });
        }

        public static void ConfigureBuild(string build)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureBuild", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("Build version must be set before SDK is initialized.");
                    return;
                }
                if (!GAValidator.ValidateBuild(build))
                {
                    GALogger.I("Validation fail - configure build: Cannot be null, empty or above 32 length. String: " + build);
                    return;
                }
                GAState.Build = build;
            });
        }

        public static void ConfigureSdkGameEngineVersion(string sdkGameEngineVersion)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureSdkGameEngineVersion", () =>
            {
                if (IsSdkReady(true, false))
                {
                    return;
                }
                if (!GAValidator.ValidateSdkWrapperVersion(sdkGameEngineVersion))
                {
                    GALogger.I("Validation fail - configure sdk version: Sdk version not supported. String: " + sdkGameEngineVersion);
                    return;
                }
                GADevice.SdkGameEngineVersion = sdkGameEngineVersion;
            });
        }

        public static void ConfigureGameEngineVersion(string gameEngineVersion)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureGameEngineVersion", () =>
            {
                if (IsSdkReady(true, false))
                {
                    return;
                }
                if (!GAValidator.ValidateEngineVersion(gameEngineVersion))
                {
                    GALogger.I("Validation fail - configure sdk version: Sdk version not supported. String: " + gameEngineVersion);
                    return;
                }
                GADevice.GameEngineVersion = gameEngineVersion;
            });
        }

        public static void ConfigureUserId(string uId)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("configureUserId", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("A custom user id must be set before SDK is initialized.");
                    return;
                }
                if (!GAValidator.ValidateUserId(uId))
                {
                    GALogger.I("Validation fail - configure user_id: Cannot be null, empty or above 64 length. Will use default user_id method. Used string: " + uId);
                    return;
                }

                GAState.UserId = uId;
            });
        }

        #endregion // CONFIGURE

        #region INITIALIZE

        private static void Initialize(string gameKey, string gameSecret)
        {
            if (_endThread)
            {
                return;
            }

#if WINDOWS_UWP || WINDOWS_WSA
            CoreApplication.Suspending += OnSuspending;
            CoreApplication.Resuming += OnResuming;
#endif
            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("initialize", () =>
            {
                if (IsSdkReady(true, false))
                {
                    GALogger.W("SDK already initialized. Can only be called once.");
                    return;
                }
                if (!GAValidator.ValidateKeys(gameKey, gameSecret))
                {
                    GALogger.W("SDK failed initialize. Game key or secret key is invalid. Can only contain characters A-z 0-9, gameKey is 32 length, gameSecret is 40 length. Failed keys - gameKey: " + gameKey + ", secretKey: " + gameSecret);
                    return;
                }

                GAState.SetKeys(gameKey, gameSecret);

                if (!GAStore.EnsureDatabase(false, gameKey))
                {
                    GALogger.W("Could not ensure/validate local event database: " + GADevice.WritablePath);
                }

                GAState.InternalInitialize();
            });
        }

#if WINDOWS_UWP || WINDOWS_WSA
        private static async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            await WaitOnSuspend();
            deferral.Complete();
        }

        private static async Task WaitOnSuspend()
        {
            if (!GAState.UseManualSessionHandling)
            {
                OnSuspend();

                while (!GAThreading.IsThreadFinished())
                {
                    await Task.Delay(100);
                }
            }
            else
            {
                GALogger.I("OnSuspending: Not calling GameAnalytics.OnStop() as using manual session handling");
            }
        }

        private static void OnResuming(object sender, object e)
        {
            GAThreading.PerformTaskOnGAThread("onResuming", () =>
            {
                if(!GAState.UseManualSessionHandling)
                {
                    OnResume();
                }
                else
                {
                    GALogger.I("OnResuming: Not calling GameAnalytics.OnResume() as using manual session handling");
                }
            });
        }
#endif

        #endregion // INITIALIZE

        #region ADD EVENTS

        public static void NewBusinessEvent(string currency, int amount, string itemType, string itemId, string cartType, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addBusinessEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add business event"))
                {
                    return;
                }
                // Send to events
                GAEvents.AddBusinessEvent(currency, amount, itemType, itemId, cartType, customFields, mergeFields);
            });
        }

        public static void NewResourceEvent(EGAResourceFlowType flowType, string currency, float amount, string itemType, string itemId, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addResourceEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add resource event"))
                {
                    return;
                }

                GAEvents.AddResourceEvent(flowType, currency, amount, itemType, itemId, customFields, mergeFields);
            });
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            NewProgressionEvent(progressionStatus, progression01, "", "", customFields, mergeFields);
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, double score, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            NewProgressionEvent(progressionStatus, progression01, "", "", score, customFields, mergeFields);
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            NewProgressionEvent(progressionStatus, progression01, progression02, "", customFields, mergeFields);
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, double score, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            NewProgressionEvent(progressionStatus, progression01, progression02, "", score, customFields, mergeFields);
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, string progression03, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addProgressionEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add progression event"))
                {
                    return;
                }

                // Send to events
                // TODO(nikolaj): check if this cast from int to double is OK
                GAEvents.AddProgressionEvent(progressionStatus, progression01, progression02, progression03, 0, false, customFields, mergeFields);
            });
        }

        public static void NewProgressionEvent(EGAProgressionStatus progressionStatus, string progression01, string progression02, string progression03, double score, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addProgressionEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add progression event"))
                {
                    return;
                }

                // Send to events
                // TODO(nikolaj): check if this cast from int to double is OK
                GAEvents.AddProgressionEvent(progressionStatus, progression01, progression02, progression03, score, true, customFields, mergeFields);
            });
        }

        public static void NewDesignEvent(string eventId, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addDesignEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add design event"))
                {
                    return;
                }
                GAEvents.AddDesignEvent(eventId, 0, false, customFields, mergeFields);
            });
        }

        public static void NewDesignEvent(string eventId, double value, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addDesignEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add design event"))
                {
                    return;
                }
                GAEvents.AddDesignEvent(eventId, value, true, customFields, mergeFields);
            });
        }

        public static void NewErrorEvent(EGAErrorSeverity severity, string message, IDictionary<string, object> customFields = null, bool mergeFields = false)
        {
            if (_endThread)
            {
                return;
            }

            GADevice.UpdateConnectionType();

            GAThreading.PerformTaskOnGAThread("addErrorEvent", () =>
            {
                if (!IsSdkReady(true, true, "Could not add error event"))
                {
                    return;
                }
                GAEvents.AddErrorEvent(severity, message, customFields, mergeFields);
            });
        }

        #endregion // ADD EVENTS

        #region SET STATE CHANGES WHILE RUNNING

        public static void SetEnabledInfoLog(bool flag)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledInfoLog", () =>
            {
                if (flag)
                {
                    GALogger.InfoLog = flag;
                    GALogger.I("Info logging enabled");
                }
                else
                {
                    GALogger.I("Info logging disabled");
                    GALogger.InfoLog = flag;
                }
            });
        }

        public static void SetEnabledVerboseLog(bool flag)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledVerboseLog", () =>
            {
                if (flag)
                {
                    GALogger.VerboseLog = flag;
                    GALogger.I("Verbose logging enabled");
                }
                else
                {
                    GALogger.I("Verbose logging disabled");
                    GALogger.VerboseLog = flag;
                }
            });
        }

        public static void SetEnabledManualSessionHandling(bool flag)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledManualSessionHandling", () =>
            {
                GAState.SetManualSessionHandling(flag);
            });
        }

        public static void SetEnabledEventSubmission(bool flag)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setEnabledEventSubmission", () =>
            {
                if (flag)
                {
                    GAState.SetEnabledEventSubmission(flag);
                    GALogger.I("Event submission enabled");
                }
                else
                {
                    GALogger.I("Event submission disabled");
                    GAState.SetEnabledEventSubmission(flag);
                }
            });
        }

        public static void SetCustomDimension01(string dimension)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension01", () =>
            {
                if (!GAValidator.ValidateDimension01(dimension))
                {
                    GALogger.W("Could not set custom01 dimension value to '" + dimension + "'. Value not found in available custom01 dimension values");
                    return;
                }
                GAState.SetCustomDimension01(dimension);
            });
        }

        public static void SetCustomDimension02(string dimension)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension02", () =>
            {
                if (!GAValidator.ValidateDimension02(dimension))
                {
                    GALogger.W("Could not set custom02 dimension value to '" + dimension + "'. Value not found in available custom02 dimension values");
                    return;
                }
                GAState.SetCustomDimension02(dimension);
            });
        }

        public static void SetCustomDimension03(string dimension)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setCustomDimension03", () =>
            {
                if (!GAValidator.ValidateDimension03(dimension))
                {
                    GALogger.W("Could not set custom03 dimension value to '" + dimension + "'. Value not found in available custom03 dimension values");
                    return;
                }
                GAState.SetCustomDimension03(dimension);
            });
        }

        public static void SetGlobalCustomEventFields(IDictionary<string, object> customFields)
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("setGlobalCustomEventFields", () =>
            {
                GAState.SetGlobalCustomEventFields(customFields);
            });
        }

        #endregion // SET STATE CHANGES WHILE RUNNING

        public static void StartSession()
        {
            if (_endThread)
            {
                return;
            }

            GAThreading.PerformTaskOnGAThread("startSession", () =>
            {
#if WINDOWS_UWP || WINDOWS_WSA
                if(GAState.UseManualSessionHandling)
#endif
                {
                    if (!GAState.Initialized)
                    {
                        return;
                    }

                    if (GAState.IsEnabled() && GAState.SessionIsStarted())
                    {
                        GAState.EndSessionAndStopQueue(false);
                    }

                    GAState.ResumeSessionAndStartQueue();
                }
            });
        }

        public static void EndSession()
        {
#if WINDOWS_UWP || WINDOWS_WSA
            if(GAState.UseManualSessionHandling)
#endif
            {
                OnSuspend();
            }
        }

        public static void OnResume()
        {
            if (_endThread)
            {
                return;
            }

            GALogger.D("OnResume() called");
            GAThreading.PerformTaskOnGAThread("onResume", () =>
            {
                GAState.ResumeSessionAndStartQueue();
            });
        }

        public static void OnSuspend()
        {
            if (_endThread)
            {
                return;
            }

            GALogger.D("OnSuspend() called");
            GAThreading.PerformTaskOnGAThread("onSuspend", () =>
            {
                try
                {
                    GAState.EndSessionAndStopQueue(false);
                }
                catch (Exception)
                {
                }
            });
        }

        public static void OnQuit()
        {
            if (_endThread)
            {
                return;
            }

            GALogger.D("OnQuit() called");
            GAThreading.PerformTaskOnGAThread("onQuit", () =>
            {
                try
                {
                    _endThread = true;
                    GAState.EndSessionAndStopQueue(true);
                }
                catch (Exception)
                {
                }
            });
        }

        #region REMOTE CONFIGS

        public static string GetRemoteConfigsValueAsString(string key, string defaultValue = null)
        {
            return GAState.GetRemoteConfigsStringValue(key, defaultValue);
        }

        public static bool IsRemoteConfigsReady()
        {
            return GAState.IsRemoteConfigsReady();
        }

        public static void AddRemoteConfigsListener(IRemoteConfigsListener listener)
        {
            GAState.AddRemoteConfigsListener(listener);
        }

        public static void RemoveRemoteConfigsListener(IRemoteConfigsListener listener)
        {
            GAState.RemoveRemoteConfigsListener(listener);
        }

        public static string GetRemoteConfigsAsString()
        {
            return GAState.GetRemoteConfigsAsString();
        }

#endregion // REMOTE CONFIGS

#region AB TESTING

        public static string GetABTestingId()
        {
            return GAState.GetABTestingId();
        }

        public static string GetABTestingVariantId()
        {
            return GAState.GetABTestingVariantId();
        }

#endregion // AB TESTING

#region PRIVATE HELPERS

        private static bool IsSdkReady(bool needsInitialized)
        {
            return IsSdkReady(needsInitialized, true);
        }

        private static bool IsSdkReady(bool needsInitialized, bool warn)
        {
            return IsSdkReady(needsInitialized, warn, "");
        }

        private static bool IsSdkReady(bool needsInitialized, bool warn, String message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                message = message + ": ";
            }

            // Make sure database is ready
            if (!GAStore.IsTableReady)
            {
                if (warn)
                {
                    GALogger.W(message + "Datastore not initialized");
                }
                return false;
            }
            // Is SDK initialized
            if (needsInitialized && !GAState.Initialized)
            {
                if (warn)
                {
                    GALogger.W(message + "SDK is not initialized");
                }
                return false;
            }
            // Is SDK enabled
            if (needsInitialized && !GAState.IsEnabled())
            {
                if (warn)
                {
                    GALogger.W(message + "SDK is disabled");
                }
                return false;
            }
            // Is session started
            if (needsInitialized && !GAState.SessionIsStarted())
            {
                if (warn)
                {
                    GALogger.W(message + "Session has not started yet");
                }
                return false;
            }
            return true;
        }

#endregion // PRIVATE HELPERS
    }
}