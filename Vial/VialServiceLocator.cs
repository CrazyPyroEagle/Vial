using System;
using System.Collections;
using UnityEngine;
using Vial.Mixins;

[module: Patch("Assembly-CSharp"), Required("UnityEngine.CoreModule")]

namespace Vial
{
    public static class Placeholder { }     // Temporary: exists only to provide public access to a type in this assembly, which will be possible when an API has been written.

    [Mixin, Name(typeof(StandardServiceLocator))]
    internal class MixinStandardServiceLocator
    {
        [BaseDependency]
        private void Base(SharedInfoProfile profile, PlatformConfig config) => throw new NotImplementedException();

        [Mixin]
        private MixinStandardServiceLocator(SharedInfoProfile profile, PlatformConfig config)
        {
            Debug.Log("[Vial] This assembly has been compiled with Vial mixins");
            if (profile.AutoBugReportingEnabled)
            {
                Debug.Log("[Vial] Automatic bug reporting is currently enabled, setting to disabled");
                profile.AutoBugReportingEnabled = false;
            }
            Base(profile, config);
        }
    }

    [Mixin, Name(typeof(LoginSceneController))]
    internal class MixinLoginSceneController
    {
        [Dependency]
        private DialogController ShowDialog(string title, string message, DialogDetails.Preset preset = DialogDetails.Preset.OK) => throw new NotImplementedException();

        [Mixin, Name("LoginSceneController/<Start>c__Iterator0")]
        private class MixinStartIterator
        {
            [BaseDependency]
            private bool Base() => throw new NotImplementedException();
            
            [Dependency, Name("$this")]
            private MixinLoginSceneController @this;

            [Mixin]
            private bool MoveNext()
            {
                bool ret = Base();
                if (!ret) @this.ShowDialog("Warning", "You are using a modified version of the game. Please report bugs to the modders, NOT to BMG.").AutoClose();
                return ret;
            }
        }
    }
}
