using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Vial
{
    public class VialServiceLocator : StandardServiceLocator
    {
        public new VialApplicationService ApplicationService => (VialApplicationService)base.ApplicationService;

        public VialServiceLocator(SharedInfoProfile profile, PlatformConfig config) : base(profile, config)
        {
            Debug.Log("[Vial] Using VialServiceLocator instead of StandardServiceLocator");
            Debug.LogFormat("[Vial] Automatic bug reporting is currently {0}, setting to disabled", profile.AutoBugReportingEnabled ? "enabled" : "disabled");
            ApplicationService.AutoBugReportEnabled = profile.AutoBugReportingEnabled = false;
            Debug.LogFormat("[Vial] Using resources located at {0}", profile.AssetURL);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, LoadSceneMode mode)
        {
            Debug.Log("[Vial] Caught scene load: " + scene.name);
            if (scene.name == ApplicationService.SceneList[Scene.Login])
            {
                Debug.Log("[Vial] Login scene was loaded, mode: " + mode.ToString());
                GameObject[] rootObjects = scene.GetRootGameObjects();
                scene.GetUnique<LoginSceneController>().ShowDialog("Warning", "You are using a modified version. Please report bugs to the modders, NOT to BMG.").AutoClose();
            }
            else if (scene.name == ApplicationService.SceneList[Scene.Game])
            {
                Debug.Log("[Vial] Game scene was loaded, mode: " + mode.ToString());
                // TODO Stuff ;)
            }
        }
    }

    [Wrap]
    public class VialApplicationService : ApplicationService
    {
        public Dictionary<Scene, string> SceneList => sceneList_;

        public VialApplicationService(SharedInfoProfile profile, IAssetService assetService, INetworkService networkService) : base(profile, assetService, networkService) { }
    }

    /*[Mixin(typeof(StandardServiceLocator))]
    internal class MixinStandardServiceLocator
    {
        [Dependency]
        private MixinApplicationService ApplicationService { get; }

        [BaseDependency]
        private void Base(SharedInfoProfile profile, PlatformConfig config) => throw new NotImplementedException();

        public MixinStandardServiceLocator(SharedInfoProfile profile, PlatformConfig config)
        {
            Debug.Log("[Vial] This assembly has been compiled with Vial mixins");
            Debug.LogFormat("[Vial] Automatic bug reporting is currently {0}, setting to disabled", profile.AutoBugReportingEnabled ? "enabled" : "disabled");
            profile.AutoBugReportingEnabled = false;
            Base(profile, config);
        }
    }

    [Mixin(typeof(ApplicationService))]
    internal class MixinApplicationService
    {
        [Dependency]
        public Dictionary<Scene, string> SceneList { get; }

        [Dependency]
        public bool AutoBugReportEnabled;

        [Transparent]
        public static implicit operator ApplicationService(MixinApplicationService service) => throw new NotImplementedException();

        [Transparent]
        public static implicit operator MixinApplicationService(ApplicationService service) => throw new NotImplementedException();
    }

    [Mixin(typeof(LoginSceneController))]
    internal class MixinLoginSceneController
    {
        [BaseDependency]
        private IEnumerator Base() => throw new NotImplementedException();

        [Dependency]
        private DialogController ShowDialog(string title, string message, DialogDetails.Preset preset = DialogDetails.Preset.OK) => throw new NotImplementedException();

        private IEnumerator Start()
        {
            IEnumerator result = Base();
            ShowDialog("Warning", "You are using a modified version of the game. Please report bugs to the modders, NOT to BMG.");
            return result;
        }
    }*/

    [AttributeUsage(AttributeTargets.Class)]
    public class WrapAttribute : Attribute { }

    /*
    /// <summary>
    /// Inject the definitions inside this class into the superclass.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class), Transparent]
    public class MixinAttribute : Attribute
    {
        Type Target { get; }

        public MixinAttribute(Type target) => Target = target;
    }

    /// <summary>
    /// Don't replace this definition when injecting mixins.
    /// The definition's access modifier may be modified to match requirements this definition imposes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor), Transparent]
    public class DependencyAttribute : Attribute { }
    
    /// <summary>
    /// Substitute uses of this definition with the mixed class' base definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor), Transparent]
    public class BaseDependencyAttribute : Attribute { }
    
    /// <summary>
    /// Completely exclude this definition when injecting this assembly.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class), Transparent]
    public class TransparentAttribute : Attribute { }*/
}
