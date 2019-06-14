using System;
using Vial.Mixins;

namespace Vial
{
    [Dependency, Name(typeof(global::SharedInfoProfile))]
    class SharedInfoProfile
    {
        [Dependency]
        internal bool AutoBugReportingEnabled;
    }

    [Dependency, Name(typeof(global::PlatformConfig))]
    class PlatformConfig { }

    [Dependency, Name(typeof(UnityEngine.Debug))]
    class Debug
    {
        [Dependency]
        internal static void Log(object message) => throw new NotImplementedException();

        [Dependency]
        internal static void LogFormat(string format, params object[] args) => throw new NotImplementedException();
    }

    [Dependency, Name(typeof(global::DialogController))]
    class DialogController
    {
        [Dependency]
        internal DialogController AutoClose() => throw new NotImplementedException();
    }
    
    class DialogDetails
    {
        [Dependency, Name(typeof(global::DialogDetails.Preset))]
        internal enum Preset
        {
            OK = 1
        }
    }
}
