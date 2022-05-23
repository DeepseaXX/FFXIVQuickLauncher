﻿using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading;
using Newtonsoft.Json;
using Serilog;
using XIVLauncher.Common.PlatformAbstractions;

namespace XIVLauncher.Common.Dalamud
{
    public class DalamudLauncher
    {
        private readonly DalamudLoadMethod loadMethod;
        private readonly DirectoryInfo gamePath;
        private readonly DirectoryInfo configDirectory;
        private readonly ClientLanguage language;
        private readonly IDalamudRunner runner;
        private readonly DalamudUpdater updater;
        private readonly int injectionDelay;
        private readonly bool fakeLogin;

        public DalamudLauncher(IDalamudRunner runner, DalamudUpdater updater, DalamudLoadMethod loadMethod, DirectoryInfo gamePath, DirectoryInfo configDirectory, ClientLanguage clientLanguage, int injectionDelay, bool fakeLogin = false)
        {
            this.runner = runner;
            this.updater = updater;
            this.loadMethod = loadMethod;
            this.gamePath = gamePath;
            this.configDirectory = configDirectory;
            this.language = clientLanguage;
            this.injectionDelay = injectionDelay;
            this.fakeLogin = fakeLogin;
        }

        public const string REMOTE_BASE = "https://xlasset-1253720819.cos.ap-nanjing.myqcloud.com/DalamudVersion.json";

        public bool HoldForUpdate(DirectoryInfo gamePath)
        {
            Log.Information("[HOOKS] DalamudLauncher::HoldForUpdate(gp:{0})", gamePath.FullName);

            if (this.updater.State != DalamudUpdater.DownloadState.Done)
                this.updater.ShowOverlay();

            while (this.updater.State != DalamudUpdater.DownloadState.Done)
            {
                if (this.updater.State == DalamudUpdater.DownloadState.Failed)
                {
                    this.updater.CloseOverlay();
                    return false;
                }

                if (this.updater.State == DalamudUpdater.DownloadState.NoIntegrity)
                {
                    this.updater.CloseOverlay();
                    throw new DalamudRunnerException("No runner integrity");
                }

                Thread.Yield();
            }

            if (!this.updater.Runner.Exists)
                throw new DalamudRunnerException("Runner not present");

            if (!ReCheckVersion(gamePath))
            {
                this.updater.SetOverlayProgress(IDalamudLoadingOverlay.DalamudUpdateStep.Unavailable);
                this.updater.ShowOverlay();
                Log.Error("[HOOKS] ReCheckVersion fail");

                return false;
            }

            return true;
        }

        public Process Run(FileInfo gameExe, string gameArgs, IDictionary<string, string> environment)
        {
            Log.Information("[HOOKS] DalamudLauncher::Run(gp:{0}, cl:{1})", this.gamePath.FullName, this.language);

            var ingamePluginPath = Path.Combine(this.configDirectory.FullName, "installedPlugins");
            var defaultPluginPath = Path.Combine(this.configDirectory.FullName, "devPlugins");

            Directory.CreateDirectory(ingamePluginPath);
            Directory.CreateDirectory(defaultPluginPath);

            var startInfo = new DalamudStartInfo
            {
                Language = language,
                PluginDirectory = ingamePluginPath,
                DefaultPluginDirectory = defaultPluginPath,
                ConfigurationPath = DalamudSettings.GetConfigPath(this.configDirectory),
                AssetDirectory = this.updater.AssetDirectory.FullName,
                GameVersion = Repository.Ffxiv.GetVer(gamePath),
                WorkingDirectory = this.updater.Runner.Directory?.FullName,
                DelayInitializeMs = this.injectionDelay,
            };

            if (this.loadMethod != DalamudLoadMethod.ACLonly)
                Log.Information("[HOOKS] DelayInitializeMs: {0}", startInfo.DelayInitializeMs);

            switch (this.loadMethod)
            {
                case DalamudLoadMethod.EntryPoint:
                    Log.Verbose("[HOOKS] Now running OEP rewrite");
                    break;
                case DalamudLoadMethod.DllInject:
                    Log.Verbose("[HOOKS] Now running DLL inject");
                    break;
                case DalamudLoadMethod.ACLonly:
                    Log.Verbose("[HOOKS] Now running ACL-only fix without injection");
                    break;
            }

            var process = this.runner.Run(this.updater.Runner, this.fakeLogin, gameExe, gameArgs, environment, this.loadMethod, startInfo);

            this.updater.CloseOverlay();

            if (this.loadMethod != DalamudLoadMethod.ACLonly)
                Log.Information("[HOOKS] Started dalamud!");

            return process;
        }

        private bool ReCheckVersion(DirectoryInfo gamePath)
        {
            if (this.updater.State != DalamudUpdater.DownloadState.Done)
                return false;

            if (this.updater.RunnerOverride != null)
                return true;

            var info = DalamudVersionInfo.Load(new FileInfo(Path.Combine(this.updater.Runner.DirectoryName!,
                "version.json")));

            if (Repository.Ffxiv.GetVer(gamePath) != info.SupportedGameVer)
                return false;

            return true;
        }

        public static bool CanRunDalamud(DirectoryInfo gamePath)
        {
            using var client = new WebClient();

            var versionInfoJson = client.DownloadString(REMOTE_BASE);
            var remoteVersionInfo = JsonConvert.DeserializeObject<DalamudVersionInfo>(versionInfoJson);

            if (Repository.Ffxiv.GetVer(gamePath) != remoteVersionInfo.SupportedGameVer)
                return false;

            return true;
        }
    }
}