#nullable enable
using System;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    public sealed class CliInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "CLI Manager";
        private const string OpenWindowMenuItemPath = "Window/Unity CLI Manager";
        private static readonly Vector2 WindowMinSize = new Vector2(420f, 340f);
        private static GUIStyle? _updateAvailableLabelStyle;

        private Vector2 _scrollPosition;
        private CliInstallStatus _status;
        private string _packageVersion = string.Empty;
        private string _latestReleaseVersion = string.Empty;
        private string _installedVersion = string.Empty;
        private string _executablePath = string.Empty;
        private string _platformDisplayName = string.Empty;
        private string _downloadUrl = string.Empty;
        private string _releasePageUrl = string.Empty;
        private string _pathCommand = string.Empty;
        private string _errorMessage = string.Empty;
        private string _skillFeedbackMessage = string.Empty;
        private string _cachedUpdateAvailabilityPackageVersion = string.Empty;
        private string _cachedUpdateAvailabilityLatestReleaseVersion = string.Empty;
        private MessageType _skillFeedbackType = MessageType.Info;
        private float _downloadProgress;
        private bool _hasLoadedState;
        private bool _isDownloading;
        private bool _isFetchingLatestVersion;
        private bool _latestReleaseCheckFailed;
        private bool _isUpdateAvailable;
        private SkillTarget _skillTarget;

        [MenuItem(OpenWindowMenuItemPath)]
        private static void OpenWindow()
        {
            CliInstallerWindow window = GetWindow<CliInstallerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = WindowMinSize;
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = WindowMinSize;
            RefreshState(true);
        }

        private void OnFocus()
        {
            if (!_isDownloading)
            {
                RefreshState(false);
            }
        }

        private void OnGUI()
        {
            if (!_hasLoadedState && !_isDownloading)
            {
                RefreshState(false);
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            GUILayout.Space(8f);
            DrawPackageInfoSection();
            GUILayout.Space(8f);
            DrawCliStatusSection();
            GUILayout.Space(8f);
            DrawActionSection();
            GUILayout.Space(8f);
            DrawPathSetupSection();
            GUILayout.Space(8f);
            DrawSkillSection();

            EditorGUILayout.EndScrollView();
        }

        private void DrawPackageInfoSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Package Info", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Package Version", FormatVersion(_packageVersion));

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel("Latest Release");
                    EditorGUILayout.LabelField(
                        GetLatestReleaseVersionLabel(),
                        IsUpdateAvailable() ? UpdateAvailableLabelStyle : EditorStyles.label);

                    using (new EditorGUI.DisabledScope(_isFetchingLatestVersion))
                    {
                        if (GUILayout.Button("Refresh", GUILayout.Width(72f)))
                        {
                            RefreshLatestReleaseVersion(true);
                        }
                    }
                }

                DrawLinkButton("Repository", CliInstallerState.GetRepositoryUrl(), "Open Repository");
                DrawLinkButton("Release", _releasePageUrl, "Open Release");
            }
        }

        private void DrawCliStatusSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("CLI Status", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Status", GetStatusLabel());
                EditorGUILayout.LabelField("Platform", _platformDisplayName);
                DrawSelectableValue("Path", _executablePath);
            }
        }

        private void DrawActionSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Actions", EditorStyles.boldLabel);

                if (!string.IsNullOrWhiteSpace(_errorMessage))
                {
                    EditorGUILayout.HelpBox(_errorMessage, MessageType.Error);
                }

                string releaseAvailabilityMessage = GetReleaseAvailabilityMessage();
                if (!string.IsNullOrWhiteSpace(releaseAvailabilityMessage))
                {
                    EditorGUILayout.HelpBox(releaseAvailabilityMessage, MessageType.Info);
                }

                if (_isDownloading)
                {
                    Rect progressRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
                    EditorGUI.ProgressBar(progressRect, _downloadProgress, "Downloading CLI...");
                    GUILayout.Space(4f);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(_errorMessage))
                {
                    if (GUILayout.Button("Retry", GUILayout.Height(28f)))
                    {
                        BeginInstall();
                    }

                    return;
                }

                if (_isFetchingLatestVersion && string.IsNullOrWhiteSpace(_latestReleaseVersion))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button("Checking release...", GUILayout.Height(28f));
                    }

                    return;
                }

                if (_latestReleaseCheckFailed && string.IsNullOrWhiteSpace(_latestReleaseVersion))
                {
                    if (GUILayout.Button("Retry release check", GUILayout.Height(28f)))
                    {
                        RefreshLatestReleaseVersion(true);
                    }

                    return;
                }

                if (string.IsNullOrWhiteSpace(_latestReleaseVersion))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button("No release available", GUILayout.Height(28f));
                    }

                    return;
                }

                switch (_status)
                {
                    case CliInstallStatus.NotInstalled:
                        if (GUILayout.Button("Install CLI", GUILayout.Height(28f)))
                        {
                            BeginInstall();
                        }

                        return;
                    case CliInstallStatus.UpdateRequired:
                        if (GUILayout.Button("Update CLI", GUILayout.Height(28f)))
                        {
                            BeginInstall();
                        }

                        return;
                    case CliInstallStatus.UpToDate:
                        using (new EditorGUI.DisabledScope(true))
                        {
                            GUILayout.Button("Up to date", GUILayout.Height(28f));
                        }

                        return;
                    default:
                        throw new InvalidOperationException("Unsupported CLI install status: " + _status);
                }
            }
        }

        private void DrawPathSetupSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("PATH Setup", EditorStyles.boldLabel);

                EditorGUILayout.HelpBox("Add the install directory to your PATH to use the CLI from the terminal. A short, fixed path also saves tokens when AI agents invoke the CLI repeatedly.", MessageType.None);
                DrawSelectableValue("Command", _pathCommand);

                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(_pathCommand));
                if (GUILayout.Button("Copy PATH command"))
                {
                    EditorGUIUtility.systemCopyBuffer = _pathCommand;
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawSkillSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("AI Agent Skill", EditorStyles.boldLabel);
                _skillTarget = (SkillTarget)EditorGUILayout.EnumPopup("Target", _skillTarget);

                if (GUILayout.Button("Install Skill", GUILayout.Height(24f)))
                {
                    InstallSkill();
                }

                if (!string.IsNullOrWhiteSpace(_skillFeedbackMessage))
                {
                    EditorGUILayout.HelpBox(_skillFeedbackMessage, _skillFeedbackType);
                }
            }
        }

        private void DrawLinkButton(string label, string url, string buttonLabel)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(label);

                EditorGUI.BeginDisabledGroup(string.IsNullOrWhiteSpace(url));
                if (GUILayout.Button(buttonLabel))
                {
                    Application.OpenURL(url);
                }

                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawSelectableValue(string label, string value)
        {
            EditorGUILayout.LabelField(label);
            EditorGUILayout.SelectableLabel(
                value,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }

        private void InstallSkill()
        {
            try
            {
                SkillInstaller.Install(_skillTarget);
                string destination = SkillInstaller.GetDestination(_skillTarget);
                _skillFeedbackType = MessageType.Info;
                _skillFeedbackMessage = "Installed to: " + destination;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _skillFeedbackType = MessageType.Error;
                _skillFeedbackMessage = exception.Message;
            }
        }

        private void BeginInstall()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_latestReleaseVersion) || string.IsNullOrWhiteSpace(_downloadUrl))
                {
                    _errorMessage = string.Empty;
                    Repaint();
                    return;
                }

                _errorMessage = string.Empty;
                _downloadProgress = 0f;
                _isDownloading = true;
                Repaint();

                CliDownloader.DownloadAndInstallAsync(
                    _downloadUrl,
                    _latestReleaseVersion,
                    CliInstallerState.GetInstallDirectory(),
                    HandleDownloadProgress,
                    HandleDownloadError,
                    HandleDownloadComplete);
            }
            catch (Exception exception)
            {
                _isDownloading = false;
                _errorMessage = exception.Message;
                Repaint();
            }
        }

        private void HandleDownloadProgress(float progress)
        {
            _downloadProgress = progress;
            Repaint();
        }

        private void HandleDownloadError(string errorMessage)
        {
            _isDownloading = false;
            _errorMessage = errorMessage;
            RefreshState(false);
            Repaint();
        }

        private void HandleDownloadComplete()
        {
            _isDownloading = false;
            _downloadProgress = 1f;
            RefreshState(true);
            Repaint();
        }

        private void RefreshState(bool shouldClearError)
        {
            if (shouldClearError)
            {
                _errorMessage = string.Empty;
            }

            try
            {
                _packageVersion = CliInstallerState.GetPackageVersion();
                _installedVersion = CliInstallerState.GetInstalledVersion() ?? string.Empty;
                _executablePath = CliInstallerState.GetExecutablePath();
                _platformDisplayName = CliInstallerState.GetPlatformDisplayName();
                _pathCommand = GetPathCommand();
                RefreshLatestReleaseVersion();
                _hasLoadedState = true;
            }
            catch (Exception exception)
            {
                _hasLoadedState = true;
                _packageVersion = string.Empty;
                _latestReleaseVersion = string.Empty;
                _installedVersion = string.Empty;
                _executablePath = string.Empty;
                _platformDisplayName = string.Empty;
                _downloadUrl = string.Empty;
                _releasePageUrl = string.Empty;
                _pathCommand = string.Empty;
                _status = CliInstallStatus.NotInstalled;
                _isFetchingLatestVersion = false;
                _latestReleaseCheckFailed = false;
                ResetUpdateAvailabilityCache();

                if (string.IsNullOrWhiteSpace(_errorMessage))
                {
                    _errorMessage = exception.Message;
                }
            }
        }

        private void RefreshLatestReleaseVersion(bool forceRefresh = false)
        {
            _latestReleaseVersion = CliInstallerState.GetCachedLatestReleaseVersion() ?? string.Empty;
            RefreshReleaseDerivedState();
            if (_isFetchingLatestVersion || (!forceRefresh && !CliInstallerState.IsLatestReleaseCacheExpired()))
            {
                return;
            }

            _isFetchingLatestVersion = true;
            _latestReleaseCheckFailed = false;
            CliInstallerState.FetchLatestReleaseVersion(HandleLatestReleaseVersionFetched);
        }

        private void HandleLatestReleaseVersionFetched(CliInstallerState.LatestReleaseFetchResult result)
        {
            _latestReleaseVersion = result.LatestReleaseVersion ?? string.Empty;
            _latestReleaseCheckFailed = !result.Succeeded;
            _isFetchingLatestVersion = false;
            RefreshReleaseDerivedState();
            Repaint();
        }

        private void RefreshReleaseDerivedState()
        {
            if (string.IsNullOrWhiteSpace(_latestReleaseVersion))
            {
                _downloadUrl = string.Empty;
                _releasePageUrl = string.Empty;
            }
            else
            {
                _downloadUrl = CliInstallerState.GetDownloadUrl(_latestReleaseVersion);
                _releasePageUrl = CliInstallerState.GetReleasePageUrl(_latestReleaseVersion);
            }

            _status = CliInstallerState.GetStatus(_latestReleaseVersion);
            UpdateUpdateAvailabilityCache();
        }

        private string GetStatusLabel()
        {
            switch (_status)
            {
                case CliInstallStatus.NotInstalled:
                    return "Not Installed";
                case CliInstallStatus.UpToDate:
                    return string.IsNullOrWhiteSpace(_installedVersion)
                        ? "Installed"
                        : "Installed (" + FormatVersion(_installedVersion) + ")";
                case CliInstallStatus.UpdateRequired:
                    string targetVersion = string.IsNullOrWhiteSpace(_latestReleaseVersion)
                        ? _packageVersion
                        : _latestReleaseVersion;
                    return string.IsNullOrWhiteSpace(_installedVersion)
                        ? "Update Required"
                        : "Update Required (" + FormatVersion(_installedVersion) + " -> " + FormatVersion(targetVersion) + ")";
                default:
                    throw new InvalidOperationException("Unsupported CLI install status: " + _status);
            }
        }

        private string GetLatestReleaseVersionLabel()
        {
            if (_isFetchingLatestVersion && string.IsNullOrWhiteSpace(_latestReleaseVersion))
            {
                return "Checking...";
            }

            if (_latestReleaseCheckFailed && string.IsNullOrWhiteSpace(_latestReleaseVersion))
            {
                return "Check failed";
            }

            return FormatVersion(_latestReleaseVersion);
        }

        private bool IsUpdateAvailable()
        {
            return _isUpdateAvailable;
        }

        private string GetReleaseAvailabilityMessage()
        {
            if (_isFetchingLatestVersion && string.IsNullOrWhiteSpace(_latestReleaseVersion))
            {
                return "Checking the latest published CLI release...";
            }

            if (_latestReleaseCheckFailed)
            {
                return string.IsNullOrWhiteSpace(_latestReleaseVersion)
                    ? "Could not check the latest published CLI release. You can retry the release check."
                    : "Could not refresh the latest published CLI release. Using the cached release version.";
            }

            if (string.IsNullOrWhiteSpace(_latestReleaseVersion))
            {
                return "No published CLI release is currently available. Only draft releases may exist right now.";
            }

            if (!string.IsNullOrWhiteSpace(_packageVersion)
                && CliInstallerState.CompareVersions(_latestReleaseVersion, _packageVersion) < 0)
            {
                return "The latest published CLI release is " + FormatVersion(_latestReleaseVersion) + ". Draft releases are ignored by the installer.";
            }

            return string.Empty;
        }

        private void ResetUpdateAvailabilityCache()
        {
            _cachedUpdateAvailabilityPackageVersion = string.Empty;
            _cachedUpdateAvailabilityLatestReleaseVersion = string.Empty;
            _isUpdateAvailable = false;
        }

        private void UpdateUpdateAvailabilityCache()
        {
            if (string.Equals(_cachedUpdateAvailabilityPackageVersion, _packageVersion, StringComparison.Ordinal)
                && string.Equals(_cachedUpdateAvailabilityLatestReleaseVersion, _latestReleaseVersion, StringComparison.Ordinal))
            {
                return;
            }

            _cachedUpdateAvailabilityPackageVersion = _packageVersion;
            _cachedUpdateAvailabilityLatestReleaseVersion = _latestReleaseVersion;

            if (string.IsNullOrWhiteSpace(_latestReleaseVersion) || string.IsNullOrWhiteSpace(_packageVersion))
            {
                _isUpdateAvailable = false;
                return;
            }

            _isUpdateAvailable = CliInstallerState.CompareVersions(_packageVersion, _latestReleaseVersion) < 0;
        }

        private static string GetPathCommand()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.OSXEditor:
                    return "export PATH=\"$HOME/.unity-cli-bridge/unity-cli:$PATH\"";
                case RuntimePlatform.WindowsEditor:
                    return "$env:Path = \"$env:USERPROFILE\\.unity-cli-bridge\\unity-cli;$env:Path\"";
                default:
                    throw new PlatformNotSupportedException("CLI Installer only supports macOS arm64 and Windows x64 editors.");
            }
        }

        private static string FormatVersion(string version)
        {
            return string.IsNullOrWhiteSpace(version)
                ? "-"
                : "v" + version.Trim().TrimStart('v', 'V');
        }

        private static GUIStyle UpdateAvailableLabelStyle =>
            _updateAvailableLabelStyle ??= CreateUpdateAvailableLabelStyle();

        private static GUIStyle CreateUpdateAvailableLabelStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
            };
            style.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
            return style;
        }
    }
}
