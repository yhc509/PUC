#nullable enable
using System;
using System.Collections.Generic;
using UnityCli.Protocol;
using UnityEditor;
using UnityEngine;

namespace UnityCliBridge.Bridge.Editor
{
    public sealed class CliInstallerWindow : EditorWindow
    {
        private const string WindowTitle = "CLI Manager";
        private const string OpenWindowMenuItemPath = "Window/Unity CLI Manager";
        private const double AutoRefreshIntervalSeconds = 1d;
        private const double StateLoadFailureAutoRetryDelaySeconds = 10d;
        private static readonly Vector2 WindowMinSize = new Vector2(420f, 340f);
        private static GUIStyle? _updateAvailableLabelStyle;

        private Vector2 _scrollPosition;
        private CliInstallStatus _status;
        private IReadOnlyList<InstalledCliVersion> _installedVersions = Array.Empty<InstalledCliVersion>();
        private IReadOnlyList<string> _orphanedInstalls = Array.Empty<string>();
        private InstalledCliVersion? _pendingVersionRemoval;
        private string? _pendingOrphanRemoval;
        private string _packageVersion = string.Empty;
        private string _protocolVersion = string.Empty;
        private string _latestReleaseVersion = string.Empty;
        private string _pathTargetVersion = string.Empty;
        private string _executablePath = string.Empty;
        private string _platformDisplayName = string.Empty;
        private string _downloadUrl = string.Empty;
        private string _releasePageUrl = string.Empty;
        private string _pathCommand = string.Empty;
        private string _errorMessage = string.Empty;
        private string _skillFeedbackMessage = string.Empty;
        private string _skillDestinationPath = string.Empty;
        private string _installedSkillVersion = string.Empty;
        private string _globalSkillPath = string.Empty;
        private string _cachedUpdateAvailabilityPackageVersion = string.Empty;
        private string _cachedUpdateAvailabilityLatestReleaseVersion = string.Empty;
        private MessageType _skillFeedbackType = MessageType.Info;
        private float _downloadProgress;
        private bool _hasLoadedState;
        private bool _isDownloading;
        private bool _isFetchingLatestVersion;
        private bool _latestReleaseCheckFailed;
        private bool _isUpdateAvailable;
        private bool _stateLoadFailed;
        private bool _isSkillInstalled;
        private bool _isGlobalSkillInstalled;
        private SkillTarget _skillTarget;
        private SkillScope _skillScope;
        private double _nextAutoRefreshTime;
        private double _nextStateLoadRetryTime;

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
            DrawInstalledVersionsSection();
            GUILayout.Space(8f);
            DrawPathSetupSection();
            GUILayout.Space(8f);
            DrawSkillSection();

            EditorGUILayout.EndScrollView();

            ProcessPendingRemovals();
        }

        /// <summary>
        /// Removals run after every control for this event has been emitted. Doing the work inline
        /// from the button would reassign the lists mid-loop and draw the remaining rows shifted, and
        /// a quarantine during removal can change the control count too. ExitGUI aborts the rest of
        /// the event so the next pass lays out from the refreshed state.
        /// </summary>
        private void ProcessPendingRemovals()
        {
            InstalledCliVersion? versionToRemove = _pendingVersionRemoval;
            string? orphanToRemove = _pendingOrphanRemoval;
            if (versionToRemove == null && string.IsNullOrWhiteSpace(orphanToRemove))
            {
                return;
            }

            _pendingVersionRemoval = null;
            _pendingOrphanRemoval = null;

            if (versionToRemove != null)
            {
                RemoveVersion(versionToRemove);
            }
            else
            {
                RemoveOrphanedInstall(orphanToRemove!);
            }

            GUIUtility.ExitGUI();
        }

        private void DrawPackageInfoSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Package Info", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Package Version", FormatVersion(_packageVersion));
                EditorGUILayout.LabelField("Wire Protocol", string.IsNullOrWhiteSpace(_protocolVersion) ? "-" : _protocolVersion);

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
                            RefreshState(true);
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
                EditorGUILayout.LabelField("PATH Target", GetPathTargetLabel());
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

                if (_status == CliInstallStatus.UpdateRequired)
                {
                    EditorGUILayout.HelpBox(
                        "The PATH target does not point at a managed CLI version. An older CLI Manager (package 0.4.0 or earlier) overwrites it with a single flat binary. Click Install CLI to restore version dispatch.",
                        MessageType.Warning);
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

                if (string.IsNullOrWhiteSpace(_packageVersion))
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button("Package version unknown", GUILayout.Height(28f));
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

                if (IsPackageReleaseUnavailable())
                {
                    using (new EditorGUI.DisabledScope(true))
                    {
                        GUILayout.Button("CLI " + FormatVersion(_packageVersion) + " not published", GUILayout.Height(28f));
                    }

                    return;
                }

                switch (_status)
                {
                    case CliInstallStatus.NotInstalled:
                    case CliInstallStatus.UpdateRequired:
                        if (GUILayout.Button("Install CLI", GUILayout.Height(28f)))
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

        private void DrawInstalledVersionsSection()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Installed Versions", EditorStyles.boldLabel);

                EditorGUILayout.HelpBox(
                    "Each Unity package version needs the CLI of the same version. The PATH target runs the newest one and hands off to an older one automatically when a project's bridge speaks an older protocol. Nothing is removed automatically.",
                    MessageType.None);

                if (_installedVersions.Count == 0)
                {
                    EditorGUILayout.LabelField("No versioned CLI installs found.");
                }

                for (int i = 0; i < _installedVersions.Count; i++)
                {
                    InstalledCliVersion installedVersion = _installedVersions[i];
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        bool isPathTarget = !string.IsNullOrWhiteSpace(_pathTargetVersion)
                            && string.Equals(installedVersion.Version, _pathTargetVersion, StringComparison.Ordinal);
                        string label = FormatVersion(installedVersion.Version)
                            + "  protocol " + installedVersion.ProtocolVersion
                            + (isPathTarget ? "  [PATH target]" : string.Empty);
                        EditorGUILayout.LabelField(label);

                        if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                        {
                            _pendingVersionRemoval = installedVersion;
                        }
                    }
                }

                DrawOrphanedInstalls();
            }
        }

        private void DrawOrphanedInstalls()
        {
            if (_orphanedInstalls.Count == 0)
            {
                return;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Unidentified Binaries", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "A CLI binary was found on the PATH target with no version recorded for it — typically a manual download. Its wire protocol is unknown, so it cannot be used for hand-off, but it was kept rather than deleted in case it is the only CLI you have for an older project. Remove it once you are sure you do not need it.",
                MessageType.Warning);

            for (int i = 0; i < _orphanedInstalls.Count; i++)
            {
                string orphanedInstall = _orphanedInstalls[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.SelectableLabel(
                        orphanedInstall,
                        EditorStyles.textField,
                        GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    if (GUILayout.Button("Reveal", GUILayout.Width(72f)))
                    {
                        EditorUtility.RevealInFinder(orphanedInstall);
                    }

                    if (GUILayout.Button("Remove", GUILayout.Width(72f)))
                    {
                        _pendingOrphanRemoval = orphanedInstall;
                    }
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

                EditorGUI.BeginChangeCheck();
                _skillTarget = (SkillTarget)EditorGUILayout.EnumPopup("Target", _skillTarget);
                _skillScope = (SkillScope)EditorGUILayout.EnumPopup("Scope", _skillScope);
                if (EditorGUI.EndChangeCheck())
                {
                    _skillFeedbackType = MessageType.None;
                    _skillFeedbackMessage = string.Empty;
                    RefreshSkillState();
                }

                DrawSelectableValue("Path", _skillDestinationPath);
                EditorGUILayout.LabelField("Skill", GetSkillStatusLabel());

                if (_isSkillInstalled)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(GetSkillButtonLabel(), GUILayout.Height(24f)))
                        {
                            InstallSkill();
                        }

                        if (GUILayout.Button("Remove Skill", GUILayout.Height(24f)))
                        {
                            RemoveSkill();
                        }
                    }
                }
                else if (GUILayout.Button(GetSkillButtonLabel(), GUILayout.Height(24f)))
                {
                    InstallSkill();
                }

                if (_skillScope == SkillScope.Project && _isGlobalSkillInstalled)
                {
                    EditorGUILayout.HelpBox(
                        "A global copy of this skill is also installed at `" + _globalSkillPath + "`. It may take precedence over the project copy. Switch Scope to Global to update or remove that copy.",
                        MessageType.Warning);
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
                if (_isSkillInstalled)
                {
                    bool shouldOverwrite = EditorUtility.DisplayDialog(
                        "Overwrite Skill?",
                        "A skill is already installed at:\n" + _skillDestinationPath + "\nOverwrite it?",
                        "Overwrite",
                        "Cancel");
                    if (!shouldOverwrite)
                    {
                        return;
                    }
                }

                SkillInstaller.Install(_skillTarget, _skillScope);
                RefreshSkillState();
                _skillFeedbackType = MessageType.Info;
                _skillFeedbackMessage = "Installed to: " + _skillDestinationPath;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _skillFeedbackType = MessageType.Error;
                _skillFeedbackMessage = exception.Message;
            }
        }

        private void RemoveSkill()
        {
            string removedPath = _skillDestinationPath;
            bool shouldRemove = EditorUtility.DisplayDialog(
                "Remove Skill?",
                "This will remove the installed skill at:\n" + removedPath,
                "Remove",
                "Cancel");
            if (!shouldRemove)
            {
                return;
            }

            try
            {
                bool removed = SkillInstaller.Remove(_skillTarget, _skillScope);
                RefreshSkillState();

                if (removed)
                {
                    _skillFeedbackType = MessageType.Info;
                    _skillFeedbackMessage = "Removed from: " + removedPath;
                }
                else
                {
                    _skillFeedbackType = MessageType.Warning;
                    _skillFeedbackMessage = "Nothing to remove at: " + removedPath;
                }
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
                if (string.IsNullOrWhiteSpace(_packageVersion) || string.IsNullOrWhiteSpace(_downloadUrl))
                {
                    _errorMessage = string.Empty;
                    Repaint();
                    return;
                }

                _errorMessage = string.Empty;

                // The binary for this package version is already on disk (a stale or clobbered PATH
                // target is the usual reason we get here). Repointing does not need a 78MB download.
                if (CliInstallerState.IsVersionInstalled(_packageVersion))
                {
                    CliInstallerState.FinalizeInstall(_packageVersion);
                    RefreshState(true);
                    Repaint();
                    return;
                }

                _downloadProgress = 0f;
                _isDownloading = true;
                Repaint();

                CliDownloader.DownloadAndInstallAsync(
                    _downloadUrl,
                    _packageVersion,
                    CliInstallerState.GetVersionInstallDirectory(_packageVersion),
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

        private void RemoveVersion(InstalledCliVersion installedVersion)
        {
            bool shouldRemove = EditorUtility.DisplayDialog(
                "Remove CLI " + FormatVersion(installedVersion.Version) + "?",
                "This will delete:\n" + installedVersion.Directory
                    + "\n\nProjects on a package version that speaks protocol "
                    + installedVersion.ProtocolVersion
                    + " will stop working until it is installed again.",
                "Remove",
                "Cancel");
            if (!shouldRemove)
            {
                return;
            }

            try
            {
                _errorMessage = string.Empty;
                CliInstallerState.RemoveVersion(installedVersion.Version);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _errorMessage = exception.Message;
            }

            RefreshState(false);
            Repaint();
        }

        private void RemoveOrphanedInstall(string orphanedInstall)
        {
            bool shouldRemove = EditorUtility.DisplayDialog(
                "Remove unidentified CLI binary?",
                "This will delete:\n" + orphanedInstall
                    + "\n\nIts wire protocol is unknown. If a project on an older package version still needs it, you will have to reinstall that CLI yourself.",
                "Remove",
                "Cancel");
            if (!shouldRemove)
            {
                return;
            }

            try
            {
                _errorMessage = string.Empty;
                CliInstallerState.RemoveOrphanedInstall(orphanedInstall);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _errorMessage = exception.Message;
            }

            RefreshState(false);
            Repaint();
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

        private void OnInspectorUpdate()
        {
            if (_isDownloading)
            {
                return;
            }

            double currentTime = EditorApplication.timeSinceStartup;
            if (currentTime < _nextAutoRefreshTime)
            {
                return;
            }

            _nextAutoRefreshTime = currentTime + AutoRefreshIntervalSeconds;

            bool shouldRepaint = false;
            if (_hasLoadedState
                && currentTime >= _nextStateLoadRetryTime
                && (_stateLoadFailed || HasInstallStateChanged()))
            {
                RefreshState(false);
                shouldRepaint = true;
            }

            if (CliInstallerState.IsLatestReleaseCacheExpired())
            {
                string previousLatestReleaseVersion = _latestReleaseVersion;
                bool wasFetchingLatestVersion = _isFetchingLatestVersion;
                bool previousReleaseCheckFailed = _latestReleaseCheckFailed;

                RefreshLatestReleaseVersion();

                shouldRepaint = shouldRepaint
                    || !string.Equals(previousLatestReleaseVersion, _latestReleaseVersion, StringComparison.Ordinal)
                    || wasFetchingLatestVersion != _isFetchingLatestVersion
                    || previousReleaseCheckFailed != _latestReleaseCheckFailed;
            }

            if (shouldRepaint)
            {
                Repaint();
            }
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
                _protocolVersion = CliInstallerState.GetProtocolVersion();
                _installedVersions = CliInstallerState.ListInstalledVersions();
                _orphanedInstalls = CliInstallerState.ListOrphanedInstalls();
                _pathTargetVersion = CliInstallerState.GetPathTargetVersion() ?? string.Empty;
                _executablePath = CliInstallerState.GetExecutablePath();
                _platformDisplayName = CliInstallerState.GetPlatformDisplayName();
                _pathCommand = GetPathCommand();
                RefreshLatestReleaseVersion();
                RefreshSkillState();
                _hasLoadedState = true;
                _stateLoadFailed = false;
                _nextStateLoadRetryTime = 0d;
            }
            catch (Exception exception)
            {
                _hasLoadedState = true;
                _stateLoadFailed = true;
                _nextStateLoadRetryTime = EditorApplication.timeSinceStartup + StateLoadFailureAutoRetryDelaySeconds;
                _packageVersion = string.Empty;
                _protocolVersion = string.Empty;
                _latestReleaseVersion = string.Empty;
                _installedVersions = Array.Empty<InstalledCliVersion>();
                _orphanedInstalls = Array.Empty<string>();
                _pathTargetVersion = string.Empty;
                _executablePath = string.Empty;
                _platformDisplayName = string.Empty;
                _downloadUrl = string.Empty;
                _releasePageUrl = string.Empty;
                _pathCommand = string.Empty;
                _status = CliInstallStatus.NotInstalled;
                _isFetchingLatestVersion = false;
                _latestReleaseCheckFailed = false;
                ResetUpdateAvailabilityCache();
                RefreshSkillState();

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
            if (this == null)
            {
                return;
            }

            try
            {
                _latestReleaseVersion = result.LatestReleaseVersion ?? string.Empty;
                _latestReleaseCheckFailed = !result.Succeeded;
                _isFetchingLatestVersion = false;
                RefreshReleaseDerivedState();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                _errorMessage = exception.Message;
            }
            finally
            {
                if (this != null)
                {
                    Repaint();
                }
            }
        }

        private void RefreshReleaseDerivedState()
        {
            // The CLI must match this package's version, not the newest release: their protocol
            // versions are what have to line up, and only the same-version CLI is guaranteed to.
            _downloadUrl = string.IsNullOrWhiteSpace(_packageVersion)
                ? string.Empty
                : CliInstallerState.GetDownloadUrl(_packageVersion);
            _releasePageUrl = string.IsNullOrWhiteSpace(_latestReleaseVersion)
                ? string.Empty
                : CliInstallerState.GetReleasePageUrl(_latestReleaseVersion);

            _status = CliInstallerState.GetStatus(_packageVersion);
            UpdateUpdateAvailabilityCache();
        }

        private string GetStatusLabel()
        {
            switch (_status)
            {
                case CliInstallStatus.NotInstalled:
                    return string.IsNullOrWhiteSpace(_packageVersion)
                        ? "Not Installed"
                        : "Not Installed (needs CLI " + FormatVersion(_packageVersion) + ")";
                case CliInstallStatus.UpToDate:
                    return "Installed (" + FormatVersion(_packageVersion) + ")";
                case CliInstallStatus.UpdateRequired:
                    return "Installed (" + FormatVersion(_packageVersion) + ") - PATH target needs repair";
                default:
                    throw new InvalidOperationException("Unsupported CLI install status: " + _status);
            }
        }

        private string GetPathTargetLabel()
        {
            if (!string.IsNullOrWhiteSpace(_pathTargetVersion))
            {
                return FormatVersion(_pathTargetVersion);
            }

            return CliInstallerState.IsUnmanagedPathTargetBinaryPresent()
                ? "Unmanaged binary (no version info)"
                : "Not set";
        }

        /// <summary>
        /// True only when the release check succeeded and told us no CLI matching this package
        /// version exists yet. A failed or in-flight check must never block a valid install.
        /// </summary>
        private bool IsPackageReleaseUnavailable()
        {
            if (_isFetchingLatestVersion || _latestReleaseCheckFailed || string.IsNullOrWhiteSpace(_packageVersion))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(_latestReleaseVersion)
                || CliInstallerState.CompareVersions(_latestReleaseVersion, _packageVersion) < 0;
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
                return "CLI " + FormatVersion(_packageVersion) + " (matching this package) has not been published yet. The latest published release is "
                    + FormatVersion(_latestReleaseVersion) + ". Draft releases are ignored by the installer.";
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

        private void RefreshSkillState()
        {
            try
            {
                _skillDestinationPath = SkillInstaller.GetDestination(_skillTarget, _skillScope);
            }
            catch (Exception exception)
            {
                _skillDestinationPath = exception.Message;
            }

            _isSkillInstalled = SkillInstaller.IsSkillInstalled(_skillTarget, _skillScope);
            _installedSkillVersion = SkillInstaller.GetInstalledSkillVersion(_skillTarget, _skillScope) ?? string.Empty;

            try
            {
                _globalSkillPath = SkillInstaller.GetDestination(_skillTarget, SkillScope.Global);
            }
            catch (Exception exception)
            {
                _globalSkillPath = exception.Message;
            }

            _isGlobalSkillInstalled = SkillInstaller.IsSkillInstalled(_skillTarget, SkillScope.Global);
        }

        private string GetSkillStatusLabel()
        {
            if (!_isSkillInstalled)
            {
                return "Not installed";
            }

            if (string.IsNullOrWhiteSpace(_installedSkillVersion))
            {
                return "Installed (version unknown)";
            }

            string installedLabel = "Installed (" + FormatVersion(_installedSkillVersion) + ")";
            if (!string.IsNullOrWhiteSpace(_packageVersion)
                && CliInstallerState.CompareVersions(_installedSkillVersion, _packageVersion) < 0)
            {
                return installedLabel + " -> update available (" + FormatVersion(_packageVersion) + ")";
            }

            return installedLabel;
        }

        private string GetSkillButtonLabel()
        {
            if (!_isSkillInstalled)
            {
                return "Install Skill";
            }

            if (string.IsNullOrWhiteSpace(_installedSkillVersion))
            {
                return "Update Skill";
            }

            if (!string.IsNullOrWhiteSpace(_packageVersion)
                && CliInstallerState.CompareVersions(_installedSkillVersion, _packageVersion) < 0)
            {
                return "Update Skill";
            }

            return "Reinstall Skill";
        }

        private bool HasInstallStateChanged()
        {
            if (string.IsNullOrWhiteSpace(_packageVersion))
            {
                return false;
            }

            bool isVersionInstalled = CliInstallerState.IsVersionInstalled(_packageVersion);
            bool wasVersionInstalled = _status != CliInstallStatus.NotInstalled;
            if (isVersionInstalled != wasVersionInstalled)
            {
                return true;
            }

            string pathTargetVersion = CliInstallerState.GetPathTargetVersion() ?? string.Empty;
            return !string.Equals(pathTargetVersion, _pathTargetVersion, StringComparison.Ordinal);
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
