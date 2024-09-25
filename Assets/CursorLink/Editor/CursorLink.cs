#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using Unity.EditorCoroutines.Editor;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

namespace Cocolab.CursorUtils
{
    public class CursorLink : EditorWindow
    {
        private const string IncludeErrorsPrefKey = "CursorLink_IncludeErrors";
        private const string IncludeWarningsPrefKey = "CursorLink_IncludeWarnings";
        private const string IncludeLogsPrefKey = "CursorLink_IncludeLogs";

        private bool includeErrors;
        private bool includeWarnings;
        private bool includeLogs;

        private Button copyButton;
        private Button saveButton;
        private const string CopyButtonDefaultText = "Copy to Clipboard";
        private const string CopyButtonSuccessText = "Copied! ✓";
        private const string SaveButtonDefaultText = "Save to Workspace Context";
        private const string SaveButtonSuccessText = "Saved! ✓";
        private const float SuccessMessageDuration = 1f;

        private List<LogEntry> logEntries = new List<LogEntry>();

        private const float RefreshInterval = 0.5f;

        private string lastSavedContent = "";

        [MenuItem("Cursor/Cursor Console")]
        public static void ShowWindow()
        {
            CursorLink wnd = GetWindow<CursorLink>();
            wnd.titleContent = new GUIContent("Cursor Console");
            wnd.minSize = new Vector2(350, 250);
        }

        private void OnEnable()
        {
            LoadPreferences();
            Application.logMessageReceived += LogMessageReceived;
            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDownEvent);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.quitting += OnEditorQuitting;
        }

        private void OnDisable()
        {
            SavePreferences();
            Application.logMessageReceived -= LogMessageReceived;
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnKeyDownEvent);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.quitting -= OnEditorQuitting;
        }

        private void LoadPreferences()
        {
            includeErrors = EditorPrefs.GetBool(IncludeErrorsPrefKey, true);
            includeWarnings = EditorPrefs.GetBool(IncludeWarningsPrefKey, true);
            includeLogs = EditorPrefs.GetBool(IncludeLogsPrefKey, true);
        }

        private void SavePreferences()
        {
            EditorPrefs.SetBool(IncludeErrorsPrefKey, includeErrors);
            EditorPrefs.SetBool(IncludeWarningsPrefKey, includeWarnings);
            EditorPrefs.SetBool(IncludeLogsPrefKey, includeLogs);
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.EnteredEditMode)
            {
                SaveLogsToFile();
            }
        }

        private void OnEditorQuitting()
        {
            SaveLogsToFile();
        }

        private void LogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (condition == "Clearing console" && type == LogType.Log)
            {
                ClearConsole();
            }
            else
            {
                logEntries.Add(new LogEntry(condition, stackTrace, type, DateTime.Now));
                UpdateToggleStates();
            }
        }

        private void OnKeyDownEvent(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.S && evt.ctrlKey)
            {
                SaveLogsToFile();
                ShowSaveSuccess(); // Add this line to show the animation
                evt.StopPropagation();
            }
        }

        public void CreateGUI()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/src/Editor/CursorLink.uss");
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }
            else
            {
                var errorLabel = new Label("Error loading CursorLink.uss");
                errorLabel.AddToClassList("error-label");
                rootVisualElement.Add(errorLabel);
                return;
            }

            VisualElement root = rootVisualElement;

            var scrollView = new ScrollView(ScrollViewMode.Vertical)
            {
                name = "ScrollView"
            };
            scrollView.AddToClassList("scroll-view");
            root.Add(scrollView);

            var mainContainer = new VisualElement();
            mainContainer.AddToClassList("main-container");
            scrollView.Add(mainContainer);

            var headerLabel = new Label("Cursor Link");
            headerLabel.AddToClassList("header-label");
            mainContainer.Add(headerLabel);

            var descriptionLabel = new Label("Select log types to export and copy to clipboard in Markdown format.");
            descriptionLabel.AddToClassList("description-label");
            mainContainer.Add(descriptionLabel);

            var toggleContainer = new VisualElement();
            toggleContainer.AddToClassList("toggle-container");
            mainContainer.Add(toggleContainer);

            AddCustomToggle(toggleContainer, "Errors", includeErrors, "error-toggle", (value) => includeErrors = value);
            AddCustomToggle(toggleContainer, "Warnings", includeWarnings, "warning-toggle", (value) => includeWarnings = value);
            AddCustomToggle(toggleContainer, "Logs", includeLogs, "log-toggle", (value) => includeLogs = value);

            copyButton = CreateActionButton(CopyLogsToClipboard, CopyButtonDefaultText, CopyButtonSuccessText);
            mainContainer.Add(copyButton);

            saveButton = CreateActionButton(SaveLogsToFile, SaveButtonDefaultText, SaveButtonSuccessText);
            mainContainer.Add(saveButton);

            SaveLogsToFile();
        }

        private readonly Dictionary<string, Toggle> toggles = new();

        private void AddCustomToggle(VisualElement container, string label, bool initialValue, string className, System.Action<bool> onValueChanged)
        {
            var toggle = new VisualElement();
            toggle.AddToClassList("custom-toggle");
            toggle.AddToClassList(className);

            var labelElement = new Label($"{(initialValue ? "✓" : "○")} {label}");
            labelElement.AddToClassList("custom-toggle__label");
            toggle.Add(labelElement);

            bool isOn = initialValue;
            toggle.RegisterCallback<ClickEvent>(evt => 
            {
                isOn = !isOn;
                onValueChanged(isOn);
                toggle.EnableInClassList($"{className}--on", isOn);
                labelElement.text = $"{(isOn ? "✓" : "○")} {label}";
                SavePreferences();
                SaveLogsToFile();
                UpdateToggleStates();
            });

            toggle.EnableInClassList($"{className}--on", isOn);

            container.Add(toggle);
        }

        private void UpdateToggleStates()
        {
            bool hasErrors = logEntries.Any(entry => entry.Type == LogType.Error || entry.Type == LogType.Exception);
            bool hasWarnings = logEntries.Any(entry => entry.Type == LogType.Warning);
            bool hasLogs = logEntries.Any(entry => entry.Type == LogType.Log);

            UpdateToggleState("Errors", hasErrors);
            UpdateToggleState("Warnings", hasWarnings);
            UpdateToggleState("Logs", hasLogs);
        }

        private void UpdateToggleState(string label, bool hasEntries)
        {
            if (toggles.TryGetValue(label, out Toggle toggle))
            {
                toggle.SetEnabled(hasEntries);
                toggle.tooltip = hasEntries ? "" : "No entries of this type";
            }
        }

        private void CopyLogsToClipboard()
        {
            string clipboardContent = GetFormattedLogs();
            if (!string.IsNullOrEmpty(clipboardContent))
            {
                GUIUtility.systemCopyBuffer = clipboardContent;
                ShowCopySuccess();
            }
        }

        private void ShowCopySuccess()
        {
            ShowButtonSuccess(copyButton);
        }

        private void ShowSaveSuccess()
        {
            ShowButtonSuccess(saveButton);
        }

        private System.Collections.IEnumerator ResetButtonText(Button button, string defaultText)
        {
            yield return new EditorWaitForSeconds(SuccessMessageDuration);
            button.text = defaultText;
            button.RemoveFromClassList("success");
        }

        private void ShowButtonSuccess(Button button)
        {
            if (button == null) return;

            button.text = button == copyButton ? CopyButtonSuccessText : SaveButtonSuccessText;
            button.AddToClassList("success");
            EditorCoroutineUtility.StartCoroutine(ResetButtonText(button, button == copyButton ? CopyButtonDefaultText : SaveButtonDefaultText), this);
        }

        private void SaveLogsToFile()
        {
            string content = GetFormattedLogs();
            if (content == lastSavedContent) return;

            string editorFolder = Path.Combine(Application.dataPath, "src", "Editor");
            Directory.CreateDirectory(editorFolder);
            string filePath = Path.Combine(editorFolder, "UnityEditorConsoleLogs.md");

            if (string.IsNullOrEmpty(content))
            {
                content = "No logs found for the selected types.";
            }

            File.WriteAllText(filePath, content);
            AssetDatabase.Refresh();

            lastSavedContent = content;
            
            // Only show save success if the saveButton is not null
            ShowSaveSuccess();
        }

        private string GetFormattedLogs()
        {
            if (!logEntries.Any(entry => ShouldInclude(entry)))
            {
                return "No logs found for the selected types.";
            }

            StringBuilder markdownBuilder = new StringBuilder();
            LogEntry lastEntry = null;
            int repeatCount = 1;

            foreach (var entry in logEntries)
            {
                if (ShouldInclude(entry))
                {
                    if (lastEntry != null && entry.Condition == lastEntry.Condition && entry.Type == lastEntry.Type)
                    {
                        repeatCount++;
                    }
                    else
                    {
                        if (lastEntry != null)
                        {
                            AppendLogEntry(markdownBuilder, lastEntry, repeatCount);
                       

 }
                        lastEntry = entry;
                        repeatCount = 1;
                    }
                }
            }

            if (lastEntry != null)
            {
                AppendLogEntry(markdownBuilder, lastEntry, repeatCount);
            }

            return markdownBuilder.ToString();
        }

        private bool ShouldInclude(LogEntry entry)
        {
            return (includeErrors && (entry.Type == LogType.Error || entry.Type == LogType.Exception)) ||
                   (includeWarnings && entry.Type == LogType.Warning) ||
                   (includeLogs && entry.Type == LogType.Log);
        }

        private void AppendLogEntry(StringBuilder markdownBuilder, LogEntry entry, int repeatCount)
        {
            string logType = entry.Type == LogType.Error || entry.Type == LogType.Exception ? "❌ Error" :
                             entry.Type == LogType.Warning ? "⚠️ Warning" : "📋 Log";

            string repeatText = repeatCount > 1 ? $" ({repeatCount}x)" : "";
            markdownBuilder.AppendLine($"### {logType}{repeatText} - {entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}");
            markdownBuilder.AppendLine("```");
            markdownBuilder.AppendLine(entry.Condition);
            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                markdownBuilder.AppendLine();
                markdownBuilder.AppendLine(entry.StackTrace);
            }
            markdownBuilder.AppendLine("```");
            markdownBuilder.AppendLine();
        }

        private class LogEntry
        {
            public string Condition { get; }
            public string StackTrace { get; }
            public LogType Type { get; }
            public DateTime Timestamp { get; }

            public LogEntry(string condition, string stackTrace, LogType type, DateTime timestamp)
            {
                Condition = condition;
                StackTrace = stackTrace;
                Type = type;
                Timestamp = timestamp;
            }
        }

        public void ClearConsole()
        {
            logEntries.Clear();
            SaveLogsToFile();
            UpdateToggleStates();
        }

        private Button CreateActionButton(Action onClick, string defaultText, string successText)
        {
            var button = new Button(onClick) { text = defaultText };
            button.AddToClassList("action-button");
            return button;
        }
    }
}
#endif