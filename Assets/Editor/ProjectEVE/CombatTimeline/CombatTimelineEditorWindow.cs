// 文件说明：维护 Combat Timeline 导入、编辑、预览和校验窗口。
// 所属模块：Combat Timeline 编辑器。
// 运行影响：仅在 Unity Editor 中影响 Timeline 调参和默认资产导入。

using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Player;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProjectEVE.Editor.CombatTimeline
{
    public sealed class CombatTimelineEditorWindow : EditorWindow
    {
        private enum TimelineDragMode
        {
            None,
            Move,
            ResizeStart,
            ResizeEnd,
            AnimationSourceStart,
            AnimationSourceEnd,
            Playhead
        }

        private enum TimelineTimeDisplayMode
        {
            Seconds,
            Frames
        }

        private const float TrackLabelWidth = 110f;
        private const float TrackLaneHeight = 24f;
        private const float TrackBasePadding = 8f;
        private const float ClipEdgeHandleWidth = 6f;
        private const float AnimationSourceHandleWidth = 8f;

        private sealed class ClipDrawLayout
        {
            public CombatTimelineClip Clip;
            public CombatTimelineTrack Track;
            public Rect Rect;

            /// <summary>
            /// 执行 Clip / Draw / Layout 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
            /// </summary>
            public ClipDrawLayout(CombatTimelineClip clip, CombatTimelineTrack track, Rect rect)
            {
                Clip = clip;
                Track = track;
                Rect = rect;
            }
        }


        private readonly List<ClipDrawLayout> visibleClipLayouts = new List<ClipDrawLayout>();
        private readonly List<CombatTimelineActionAsset> timelines = new List<CombatTimelineActionAsset>();
        private readonly List<CombatTimelineValidationMessage> validationMessages = new List<CombatTimelineValidationMessage>();
        private readonly List<string> validationGuiMessages = new List<string>();
        private readonly List<MessageType> validationGuiMessageTypes = new List<MessageType>();

        private CombatTimelineActionAsset selectedTimeline;
        private CombatTimelineClip selectedClip;
        private CombatAnimationClipWindow lastSampledAnimationPreviewClip;
        private CombatTimelineClip clipboardClip;
        private Vector2 listScroll;
        private Vector2 timelineScroll;
        private Vector2 inspectorScroll;
        private Vector2 validationScroll;
        private float pixelsPerSecond = 180f;
        private float playheadTime;
        private bool snapToFrame = true;
        private TimelineTimeDisplayMode timeDisplayMode = TimelineTimeDisplayMode.Seconds;
        private bool followRuntimePlayhead;
        private string runtimeFollowStatusMessage = "Runtime Follow: Off";
        private double lastRuntimeFollowRepaintTime;
        private bool editModePreview;
        private GameObject previewTarget;
        private string previewStatusMessage;
        private MessageType previewStatusType = MessageType.Info;
        private TimelineDragMode dragMode = TimelineDragMode.None;
        private CombatTimelineClip draggedClip;
        private float dragStartMouseX;
        private float dragOriginalStartTime;
        private float dragOriginalEndTime;
        private float dragOriginalClipStartOffset;
        private float dragOriginalClipEndOffset;

        /// <summary>
        /// 执行 Open 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        [MenuItem("Project EVE/Combat/Combat Timeline Editor")]
        public static void Open()
        {
            CombatTimelineEditorWindow window = GetWindow<CombatTimelineEditorWindow>("Combat Timeline");
            window.minSize = new Vector2(1100f, 640f);
            window.RefreshTimelines();
        }

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            RefreshTimelines();
            EditorApplication.update += UpdateRuntimePlayheadFollow;
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            EditorApplication.update -= UpdateRuntimePlayheadFollow;
            StopPreview();
        }

        /// <summary>
        /// 记录 Combat Timeline 资产撤销点，确保后续写入可被 Unity Undo 正确恢复。
        /// </summary>
        private void RecordTimelineUndo(string label)
        {
            if (selectedTimeline != null)
            {
                Undo.RecordObject(selectedTimeline, label);
            }
        }

        /// <summary>
        /// 标记 Combat Timeline 资产变更，并刷新校验与窗口显示。
        /// </summary>
        private void MarkTimelineChanged(bool repaint = true)
        {
            if (selectedTimeline == null)
            {
                return;
            }

            EditorUtility.SetDirty(selectedTimeline);
            ValidateSelected();
            if (repaint)
            {
                Repaint();
            }
        }

        /// <summary>
        /// 绘制 IMGUI 调试或演示界面，并读取当前运行时快照。
        /// </summary>
        private void OnGUI()
        {
            const float toolbarHeight = 24f;
            const float panelSpacing = 4f;
            Rect toolbar = new Rect(0f, 0f, position.width, toolbarHeight);
            DrawToolbar(toolbar);

            Rect content = new Rect(0f, toolbar.yMax + panelSpacing, position.width, Mathf.Max(0f, position.height - toolbarHeight - panelSpacing));
            float mainHeight = Mathf.Max(260f, content.height * 0.72f);
            mainHeight = Mathf.Min(mainHeight, Mathf.Max(0f, content.height - 120f));
            Rect left = new Rect(content.x, content.y, 230f, mainHeight);
            Rect right = new Rect(content.xMax - 316f, content.y, 316f, mainHeight);
            Rect center = new Rect(left.xMax + panelSpacing, content.y, Mathf.Max(240f, right.x - left.xMax - panelSpacing * 2f), mainHeight);
            Rect bottom = new Rect(content.x, left.yMax + panelSpacing, content.width, Mathf.Max(80f, content.yMax - left.yMax - panelSpacing));

            GUILayout.BeginArea(left, EditorStyles.helpBox);
            DrawActionList();
            GUILayout.EndArea();

            GUILayout.BeginArea(center, EditorStyles.helpBox);
            DrawTimeline();
            GUILayout.EndArea();

            GUILayout.BeginArea(right, EditorStyles.helpBox);
            DrawInspector();
            GUILayout.EndArea();

            GUILayout.BeginArea(bottom, EditorStyles.helpBox);
            DrawValidation();
            GUILayout.EndArea();

            if (editModePreview)
            {
                SamplePreview();
            }
        }

        /// <summary>
        /// 绘制 Toolbar 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawToolbar(Rect toolbar)
        {
            GUILayout.BeginArea(toolbar, EditorStyles.toolbar);
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Refresh Assets", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                RefreshTimelines();
            }

            if (GUILayout.Button("Refresh Runtime", EditorStyles.toolbarButton, GUILayout.Width(112f)))
            {
                RefreshRuntimeCache();
            }

            GUILayout.Space(12f);
            string actionLabel = selectedTimeline == null
                ? "No Action"
                : $"{selectedTimeline.ActionId}  {selectedTimeline.TotalDuration:0.###}s  {selectedTimeline.TotalFrames}f";
            GUILayout.Label(actionLabel, EditorStyles.toolbarButton, GUILayout.Width(190f));

            GUILayout.Space(10f);
            GUILayout.Label("Unit", GUILayout.Width(30f));
            timeDisplayMode = (TimelineTimeDisplayMode)EditorGUILayout.EnumPopup(timeDisplayMode, EditorStyles.toolbarPopup, GUILayout.Width(72f));

            GUILayout.Space(8f);
            GUILayout.Label("Samples", GUILayout.Width(52f));
            DrawFrameRateToolbarField();

            GUILayout.Space(8f);
            snapToFrame = GUILayout.Toggle(snapToFrame, "Snap", EditorStyles.toolbarButton, GUILayout.Width(54f));

            GUILayout.Space(10f);
            GUILayout.Label("Zoom", GUILayout.Width(38f));
            pixelsPerSecond = GUILayout.HorizontalSlider(pixelsPerSecond, 80f, 420f, GUILayout.Width(130f));

            GUILayout.Space(10f);
            GUILayout.Label("Time", GUILayout.Width(34f));
            EditorGUI.BeginDisabledGroup(followRuntimePlayhead && EditorApplication.isPlaying);
            playheadTime = Mathf.Clamp(TimeToolbarField(playheadTime), 0f, selectedTimeline != null ? selectedTimeline.TotalDuration : 0f);
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8f);
            EditorGUI.BeginDisabledGroup(!EditorApplication.isPlaying);
            bool nextFollowRuntime = GUILayout.Toggle(followRuntimePlayhead, "Follow Runtime", EditorStyles.toolbarButton, GUILayout.Width(112f));
            EditorGUI.EndDisabledGroup();
            if (!EditorApplication.isPlaying)
            {
                nextFollowRuntime = false;
            }

            if (nextFollowRuntime != followRuntimePlayhead)
            {
                followRuntimePlayhead = nextFollowRuntime;
                runtimeFollowStatusMessage = followRuntimePlayhead ? "Runtime Follow: waiting for state match" : "Runtime Follow: Off";
                Repaint();
            }

            if (followRuntimePlayhead)
            {
                GUILayout.Label(runtimeFollowStatusMessage, EditorStyles.miniLabel, GUILayout.Width(260f));
            }

            GUILayout.FlexibleSpace();
            EditorGUI.BeginChangeCheck();
            editModePreview = GUILayout.Toggle(editModePreview, "Edit Preview", EditorStyles.toolbarButton, GUILayout.Width(95f));
            if (EditorGUI.EndChangeCheck() && !editModePreview)
            {
                StopPreview();
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        /// <summary>
        /// 绘制 Frame / Rate / Toolbar / Field 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawFrameRateToolbarField()
        {
            if (selectedTimeline == null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(60f, GUILayout.Width(48f));
                EditorGUI.EndDisabledGroup();
                return;
            }

            SerializedObject serialized = new SerializedObject(selectedTimeline);
            SerializedProperty frameRateProperty = serialized.FindProperty("frameRate");
            if (frameRateProperty == null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(GetFrameRate(), GUILayout.Width(48f));
                EditorGUI.EndDisabledGroup();
                return;
            }

            serialized.Update();
            EditorGUI.BeginChangeCheck();
            float nextFrameRate = EditorGUILayout.FloatField(frameRateProperty.floatValue, GUILayout.Width(48f));
            if (EditorGUI.EndChangeCheck())
            {
                RecordTimelineUndo("Edit Combat Timeline Frame Rate");
                selectedTimeline.SetFrameRatePreserveDuration(nextFrameRate);
                playheadTime = Mathf.Clamp(SnapToFrame(playheadTime), 0f, selectedTimeline.TotalDuration);
                MarkTimelineChanged();
            }
        }

        /// <summary>
        /// 执行 Time / Toolbar / Field 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float TimeToolbarField(float value)
        {
            if (selectedTimeline == null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.FloatField(0f, GUILayout.Width(54f));
                EditorGUI.EndDisabledGroup();
                return 0f;
            }

            if (timeDisplayMode == TimelineTimeDisplayMode.Frames)
            {
                int frame = EditorGUILayout.IntField(TimeToFrame(value), GUILayout.Width(54f));
                return SnapToFrame(FrameToTime(frame));
            }

            float nextValue = EditorGUILayout.FloatField(value, GUILayout.Width(54f));
            return SnapToFrame(nextValue);
        }

        /// <summary>
        /// 处理 Playhead / Mouse 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
        private void HandlePlayheadMouse(Rect area)
        {
            if (selectedTimeline == null)
            {
                return;
            }

            Event current = Event.current;
            Rect ruler = GetRulerRect(area);
            float x = ruler.x + playheadTime * pixelsPerSecond;
            Rect lineHit = new Rect(x - 5f, area.y, 10f, area.height);
            Rect headHit = new Rect(x - 7f, ruler.y, 14f, ruler.height);
            EditorGUIUtility.AddCursorRect(ruler, MouseCursor.ResizeHorizontal);
            EditorGUIUtility.AddCursorRect(lineHit, MouseCursor.ResizeHorizontal);

            if (current.type == EventType.MouseDown && current.button == 0 && (ruler.Contains(current.mousePosition) || lineHit.Contains(current.mousePosition) || headHit.Contains(current.mousePosition)))
            {
                followRuntimePlayhead = false;
                runtimeFollowStatusMessage = "Runtime Follow: Off";
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                dragMode = TimelineDragMode.Playhead;
                draggedClip = null;
                SetPlayheadFromMouse(area, current.mousePosition.x);
                current.Use();
                return;
            }

            if (dragMode == TimelineDragMode.Playhead && current.type == EventType.MouseDrag && current.button == 0)
            {
                SetPlayheadFromMouse(area, current.mousePosition.x);
                current.Use();
                return;
            }

            if (dragMode == TimelineDragMode.Playhead && current.type == EventType.MouseUp && current.button == 0)
            {
                dragMode = TimelineDragMode.None;
                current.Use();
            }
        }

        /// <summary>
        /// 设置 Playhead / From / Mouse 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetPlayheadFromMouse(Rect area, float mouseX)
        {
            Rect ruler = GetRulerRect(area);
            float rawTime = (mouseX - ruler.x) / Mathf.Max(1f, pixelsPerSecond);
            playheadTime = SnapToFrame(Mathf.Clamp(rawTime, 0f, selectedTimeline.TotalDuration));
            Repaint();
        }

        /// <summary>
        /// 在 Play Mode 中根据当前 Player / Boss 运行时状态跟随 playhead。
        /// </summary>
        private void UpdateRuntimePlayheadFollow()
        {
            if (!followRuntimePlayhead)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                followRuntimePlayhead = false;
                runtimeFollowStatusMessage = "Runtime Follow: Off";
                Repaint();
                return;
            }

            if (selectedTimeline == null)
            {
                SetRuntimeFollowStatus("Runtime Follow: select a timeline");
                return;
            }

            if (!TryFindRuntimePlaybackForSelectedTimeline(out CombatTimelineRuntimePlaybackSnapshot snapshot))
            {
                SetRuntimeFollowStatus($"Runtime Follow: waiting for {selectedTimeline.ActionId}");
                return;
            }

            float nextTime = Mathf.Clamp(snapshot.ElapsedTime, 0f, selectedTimeline.TotalDuration);
            if (snapToFrame)
            {
                nextTime = SnapToFrame(nextTime);
            }

            if (!Mathf.Approximately(playheadTime, nextTime))
            {
                playheadTime = nextTime;
                Repaint();
            }

            SetRuntimeFollowStatus($"{snapshot.Source} {snapshot.RuntimeState}: {FormatTimelineTime(snapshot.ElapsedTime)}");
        }

        /// <summary>
        /// 更新 Runtime Follow 状态栏，并限制重复 Repaint 频率。
        /// </summary>
        private void SetRuntimeFollowStatus(string message)
        {
            if (runtimeFollowStatusMessage == message &&
                EditorApplication.timeSinceStartup - lastRuntimeFollowRepaintTime < 0.2f)
            {
                return;
            }

            runtimeFollowStatusMessage = message;
            lastRuntimeFollowRepaintTime = EditorApplication.timeSinceStartup;
            Repaint();
        }

        /// <summary>
        /// 查找与当前选中 Timeline ActionId 匹配的 Player 或 Boss 运行时播放事实。
        /// </summary>
        private bool TryFindRuntimePlaybackForSelectedTimeline(out CombatTimelineRuntimePlaybackSnapshot snapshot)
        {
            snapshot = default;
            if (selectedTimeline == null)
            {
                return false;
            }

            string selectedActionId = selectedTimeline.ActionId;
            PlayerStateMachine[] players = UnityEngine.Object.FindObjectsByType<PlayerStateMachine>(FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] != null &&
                    players[i].TryGetCombatTimelinePlaybackSnapshot(out snapshot) &&
                    string.Equals(snapshot.ActionId, selectedActionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            BossActor[] bosses = UnityEngine.Object.FindObjectsByType<BossActor>(FindObjectsSortMode.None);
            for (int i = 0; i < bosses.Length; i++)
            {
                if (bosses[i] != null &&
                    bosses[i].TryGetCombatTimelinePlaybackSnapshot(out snapshot) &&
                    string.Equals(snapshot.ActionId, selectedActionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            snapshot = default;
            return false;
        }





        /// <summary>
        /// 绘制 Action / List 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawActionList()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);

            CombatTimelineActionKind? lastKind = null;
            for (int i = 0; i < timelines.Count; i++)
            {
                CombatTimelineActionAsset timeline = timelines[i];
                if (timeline == null)
                {
                    continue;
                }

                if (lastKind != timeline.ActionKind)
                {
                    GUILayout.Space(6f);
                    EditorGUILayout.LabelField(timeline.ActionKind.ToString(), EditorStyles.miniBoldLabel);
                    lastKind = timeline.ActionKind;
                }

                bool selected = timeline == selectedTimeline;
                if (GUILayout.Toggle(selected, string.IsNullOrEmpty(timeline.DisplayName) ? timeline.ActionId : timeline.DisplayName, "Button") != selected)
                {
                    SelectTimeline(timeline);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制 Timeline 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawTimeline()
        {
            if (selectedTimeline == null)
            {
                EditorGUILayout.HelpBox("Select a combat action.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField($"{selectedTimeline.DisplayName}  {FormatTimelineTime(selectedTimeline.TotalDuration)}", EditorStyles.boldLabel);

            CombatTimelineTrack[] tracks = selectedTimeline.Tracks ?? Array.Empty<CombatTimelineTrack>();
            float totalWidth = Mathf.Max(position.width - 600f, GetTimelineVisualEndTime(tracks) * pixelsPerSecond + 160f);
            float totalHeight = Mathf.Max(240f, CalculateTimelineHeight(tracks) + 28f);
            timelineScroll = EditorGUILayout.BeginScrollView(timelineScroll);
            Rect area = GUILayoutUtility.GetRect(totalWidth, totalHeight);

            visibleClipLayouts.Clear();
            DrawTimeRuler(area);
            HandlePlayheadMouse(area);
            DrawTracks(area);
            HandleTimelineClipMouse();
            DrawPlayhead(area);

            EditorGUILayout.EndScrollView();
            HandleTimelineKeys();
        }

        /// <summary>
        /// 绘制 Time / Ruler 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawTimeRuler(Rect area)
        {
            Rect ruler = GetRulerRect(area);
            EditorGUI.DrawRect(new Rect(area.x, area.y, 108f, ruler.height), new Color(0.12f, 0.12f, 0.12f));
            EditorGUI.DrawRect(ruler, new Color(0.14f, 0.14f, 0.14f));

            float frameRate = GetFrameRate();
            if (timeDisplayMode == TimelineTimeDisplayMode.Frames)
            {
                int totalFrames = selectedTimeline.TotalFrames;
                float framePixelWidth = pixelsPerSecond / frameRate;
                int labelStep = CalculateFrameLabelStep(framePixelWidth);
                for (int frame = 0; frame <= totalFrames; frame++)
                {
                    float time = FrameToTime(frame);
                    float x = ruler.x + time * pixelsPerSecond;
                    bool major = frame % labelStep == 0;
                    if (!major && framePixelWidth < 4f)
                    {
                        continue;
                    }

                    Handles.color = major ? Color.gray : new Color(0.34f, 0.34f, 0.34f);
                    Handles.DrawLine(new Vector3(x, ruler.y + (major ? 0f : 10f)), new Vector3(x, major ? area.yMax : ruler.yMax));
                    if (major)
                    {
                        GUI.Label(new Rect(x + 2f, ruler.y + 2f, 48f, 16f), frame.ToString(), EditorStyles.miniLabel);
                    }
                }

                return;
            }

            float minorStep = pixelsPerSecond / frameRate >= 5f ? 1f / frameRate : 0.1f;
            int marks = Mathf.CeilToInt(selectedTimeline.TotalDuration / minorStep);
            for (int i = 0; i <= marks; i++)
            {
                float time = i * minorStep;
                float x = ruler.x + time * pixelsPerSecond;
                bool major = Mathf.Approximately(time % 0.5f, 0f) || Mathf.Approximately(time % 0.5f, 0.5f);
                Handles.color = major ? Color.gray : new Color(0.35f, 0.35f, 0.35f);
                Handles.DrawLine(new Vector3(x, ruler.y + (major ? 0f : 10f)), new Vector3(x, major ? area.yMax : ruler.yMax));
                if (major)
                {
                    GUI.Label(new Rect(x + 2f, ruler.y + 2f, 50f, 16f), time.ToString("0.0"), EditorStyles.miniLabel);
                }
            }
        }

        /// <summary>
        /// 绘制 Tracks 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawTracks(Rect area)
        {
            CombatTimelineTrack[] tracks = selectedTimeline.Tracks ?? Array.Empty<CombatTimelineTrack>();
            float y = area.y + 26f;
            for (int i = 0; i < tracks.Length; i++)
            {
                CombatTimelineTrack track = tracks[i];
                int laneCount = CalculateTrackLaneCount(track);
                float trackHeight = CalculateTrackHeight(laneCount);
                Rect labelRect = new Rect(area.x, y, TrackLabelWidth - 4f, trackHeight);
                Rect trackRect = new Rect(area.x + TrackLabelWidth, y, area.width - TrackLabelWidth - 10f, trackHeight);
                EditorGUI.DrawRect(trackRect, i % 2 == 0 ? new Color(0.18f, 0.18f, 0.18f) : new Color(0.15f, 0.15f, 0.15f));

                string label = track?.Name ?? "Track";
                if (laneCount > 1)
                {
                    label = $"{label} ({laneCount} lanes)";
                }

                DrawTrackLabel(track, labelRect, label);

                if (track?.Clips != null)
                {
                    List<ClipDrawLayout> layouts = BuildClipDrawLayouts(track, track.Clips, trackRect);
                    visibleClipLayouts.AddRange(layouts);
                    for (int j = 0; j < layouts.Count; j++)
                    {
                        DrawClip(layouts[j].Clip, layouts[j].Rect);
                    }
                }

                y += trackHeight + 6f;
            }
        }

        /// <summary>
        /// 绘制 Track / Label 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawTrackLabel(CombatTimelineTrack track, Rect labelRect, string label)
        {
            Rect textRect = new Rect(labelRect.x, labelRect.y, Mathf.Max(0f, labelRect.width - 24f), labelRect.height);
            GUI.Label(textRect, label, EditorStyles.miniBoldLabel);

            if (track == null || track.Locked)
            {
                return;
            }

            Rect addRect = new Rect(labelRect.xMax - 22f, labelRect.y + 2f, 20f, 18f);
            if (GUI.Button(addRect, "+", EditorStyles.miniButton))
            {
                ShowAddClipMenu(track);
            }
        }

        /// <summary>
        /// 执行 Show / Add / Clip / Menu 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void ShowAddClipMenu(CombatTimelineTrack track)
        {
            GenericMenu menu = new GenericMenu();
            switch (track.TrackKind)
            {
                case CombatTimelineTrackKind.Phase:
                    AddClipMenuItem<CombatPhaseClip>(menu, track, "Phase", CombatTimelineCapabilityId.None);
                    break;
                case CombatTimelineTrackKind.HitNode:
                    AddClipMenuItem<CombatHitNodeClip>(menu, track, "HitNode / Attack Hitbox", CombatTimelineCapabilityId.Hit_Attack);
                    break;
                case CombatTimelineTrackKind.InputBuffer:
                    AddClipMenuItem<CombatAttackWindowClip>(menu, track, "InputBuffer / Attack", CombatTimelineCapabilityId.Input_AttackBuffer);
                    AddClipMenuItem<CombatAttackWindowClip>(menu, track, "InputBuffer / Evade", CombatTimelineCapabilityId.Input_EvadeBuffer);
                    AddClipMenuItem<CombatAttackWindowClip>(menu, track, "InputBuffer / Skill", CombatTimelineCapabilityId.Input_SkillBuffer);
                    AddClipMenuItem<CombatAttackWindowClip>(menu, track, "InputBuffer / Perfect Guard Chain", CombatTimelineCapabilityId.Input_PerfectGuardChain);
                    AddClipMenuItem<CombatMarkerClip>(menu, track, "Marker", CombatTimelineCapabilityId.None);
                    break;
                case CombatTimelineTrackKind.Cancel:
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Attack Reset", CombatTimelineCapabilityId.Cancel_AttackReset);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Combo", CombatTimelineCapabilityId.Cancel_AttackCombo);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Evade", CombatTimelineCapabilityId.Cancel_ToEvade);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Skill", CombatTimelineCapabilityId.Cancel_ToSkill);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Guard", CombatTimelineCapabilityId.Cancel_ToGuard);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / To GuardRelease", CombatTimelineCapabilityId.Cancel_ToGuardRelease);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / Move", CombatTimelineCapabilityId.Cancel_MovementReturn);
                    AddClipMenuItem<CombatAttackWindowClip>(menu, track, "InputBuffer / Attack", CombatTimelineCapabilityId.Input_AttackBuffer);
                    break;
                case CombatTimelineTrackKind.Defense:
                    AddClipMenuItem<CombatDefenseWindowClip>(menu, track, "Defense / Guard Block", CombatTimelineCapabilityId.Defense_GuardBlock);
                    AddClipMenuItem<CombatDefenseWindowClip>(menu, track, "Defense / Perfect Guard", CombatTimelineCapabilityId.Defense_PerfectGuard);
                    AddClipMenuItem<CombatDefenseWindowClip>(menu, track, "Defense / Invincible", CombatTimelineCapabilityId.Defense_Invincible);
                    AddClipMenuItem<CombatDefenseWindowClip>(menu, track, "Defense / Perfect Evade", CombatTimelineCapabilityId.Defense_PerfectEvade);
                    AddClipMenuItem<CombatMarkerClip>(menu, track, "Marker", CombatTimelineCapabilityId.None);
                    break;
                case CombatTimelineTrackKind.Armor:
                    AddClipMenuItem<CombatArmorWindowClip>(menu, track, "Armor / Super Armor", CombatTimelineCapabilityId.Armor_SuperArmor);
                    AddClipMenuItem<CombatArmorWindowClip>(menu, track, "Armor / Uninterruptible", CombatTimelineCapabilityId.Armor_Uninterruptible);
                    break;
                case CombatTimelineTrackKind.PlayerMotion:
                    AddMotionClipMenuItem(menu, track, "Player Motion", CombatTimelineClipKind.PlayerMotion);
                    break;
                case CombatTimelineTrackKind.BossMotionWarp:
                    AddMotionClipMenuItem(menu, track, "Boss Motion Warp", CombatTimelineClipKind.BossMotionWarp);
                    break;
                case CombatTimelineTrackKind.BossCodeMove:
                    AddMotionClipMenuItem(menu, track, "Boss Code Move", CombatTimelineClipKind.BossCodeMove);
                    break;
                case CombatTimelineTrackKind.RootMotionSuppress:
                    AddMotionClipMenuItem(menu, track, "Root Motion Suppress", CombatTimelineClipKind.RootMotionSuppress);
                    break;
                case CombatTimelineTrackKind.Marker:
                    AddClipMenuItem<CombatMarkerClip>(menu, track, "Marker", CombatTimelineCapabilityId.None);
                    AddClipMenuItem<CombatCancelWindowClip>(menu, track, "Cancel / To GuardRelease", CombatTimelineCapabilityId.Cancel_ToGuardRelease);
                    break;
                case CombatTimelineTrackKind.Reaction:
                    AddClipMenuItem<CombatReactionWindowClip>(menu, track, "Reaction / Stun", CombatTimelineCapabilityId.Reaction_Stun);
                    AddClipMenuItem<CombatReactionWindowClip>(menu, track, "Reaction / Recovery", CombatTimelineCapabilityId.Reaction_Recovery);
                    AddClipMenuItem<CombatReactionWindowClip>(menu, track, "Reaction / Can Return", CombatTimelineCapabilityId.Reaction_CanReturn);
                    break;
                case CombatTimelineTrackKind.Interrupt:
                    AddBossReactionGateClipMenuItem(
                        menu,
                        track,
                        "Boss Gate / Allow Light Heavy Skill",
                        BossReactionGatePolicy.AllowInterrupt,
                        BossReactionGateBlockedOutcome.Any,
                        CombatAttackType.LightAttack,
                        CombatAttackType.HeavyAttack,
                        CombatAttackType.SkillAttack);
                    AddBossReactionGateClipMenuItem(
                        menu,
                        track,
                        "Boss Gate / Block Skill Knockdown",
                        BossReactionGatePolicy.BlockInterrupt,
                        BossReactionGateBlockedOutcome.Knockdown,
                        CombatAttackType.SkillAttack);
                    break;
                case CombatTimelineTrackKind.AnimationPreview:
                    AddAnimationClipMenuItem(menu, track);
                    break;
                default:
                    AddClipMenuItem<CombatMarkerClip>(menu, track, "Marker", CombatTimelineCapabilityId.None);
                    break;
            }

            menu.ShowAsContext();
        }

        private void AddClipMenuItem<TClip>(GenericMenu menu, CombatTimelineTrack track, string label, CombatTimelineCapabilityId capabilityId)
            where TClip : CombatTimelineClip, new()
        {
            menu.AddItem(new GUIContent(label), false, () => AddClipToTrack(track, CreateDefaultClip<TClip>(label, capabilityId)));
        }

        /// <summary>
        /// 添加 Motion / Clip / Menu / Item 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private void AddMotionClipMenuItem(GenericMenu menu, CombatTimelineTrack track, string label, CombatTimelineClipKind motionKind)
        {
            menu.AddItem(new GUIContent(label), false, () =>
            {
                CombatMotionReferenceClip clip = CreateDefaultClip<CombatMotionReferenceClip>(label, CombatTimelineCapabilityId.None);
                clip.MotionKind = motionKind;
                clip.BossAttackId = selectedTimeline != null ? selectedTimeline.ActionId : string.Empty;
                AddClipToTrack(track, clip);
            });
        }

        private void AddBossReactionGateClipMenuItem(
            GenericMenu menu,
            CombatTimelineTrack track,
            string label,
            BossReactionGatePolicy policy,
            BossReactionGateBlockedOutcome blockedOutcome,
            params CombatAttackType[] attackTypes)
        {
            menu.AddItem(new GUIContent(label), false, () =>
            {
                BossReactionGateWindowClip clip = CreateDefaultClip<BossReactionGateWindowClip>(label, CombatTimelineCapabilityId.None);
                clip.AttackTypes = attackTypes ?? Array.Empty<CombatAttackType>();
                clip.Policy = policy;
                clip.BlockedOutcome = blockedOutcome;
                AddClipToTrack(track, clip);
            });
        }

        /// <summary>
        /// 添加 Animation / Clip / Menu / Item 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private void AddAnimationClipMenuItem(GenericMenu menu, CombatTimelineTrack track)
        {
            menu.AddItem(new GUIContent("Animation Preview"), false, () =>
            {
                CombatAnimationClipWindow clip = CreateDefaultClip<CombatAnimationClipWindow>("Animation Preview", CombatTimelineCapabilityId.None);
                clip.AnimatorStateName = selectedTimeline != null ? selectedTimeline.AnimationStateName : string.Empty;
                clip.ClipSpeed = 1f;
                NormalizeAnimationWindowEnd(clip);
                AddClipToTrack(track, clip);
            });
        }

        private TClip CreateDefaultClip<TClip>(string name, CombatTimelineCapabilityId capabilityId)
            where TClip : CombatTimelineClip, new()
        {
            float start = selectedTimeline != null ? Mathf.Clamp(playheadTime, 0f, selectedTimeline.TotalDuration) : 0f;
            float duration = Mathf.Max(1f / GetFrameRate(), 0.05f);
            return new TClip
            {
                Name = name,
                CapabilityId = capabilityId,
                StartTime = start,
                EndTime = selectedTimeline != null ? Mathf.Min(selectedTimeline.TotalDuration, start + duration) : start + duration
            };
        }

        /// <summary>
        /// 添加 Clip / To / Track 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private void AddClipToTrack(CombatTimelineTrack track, CombatTimelineClip clip)
        {
            if (track == null || clip == null || selectedTimeline == null)
            {
                return;
            }

            RecordTimelineUndo("Add Combat Timeline Clip");
            List<CombatTimelineClip> clips = new List<CombatTimelineClip>(track.Clips ?? Array.Empty<CombatTimelineClip>()) { clip };
            track.Clips = clips.ToArray();
            selectedClip = clip;
            MarkTimelineChanged();
        }
        /// <summary>
        /// 获取 Timeline / Visual / End / Time 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetTimelineVisualEndTime(CombatTimelineTrack[] tracks)
        {
            float endTime = selectedTimeline != null ? selectedTimeline.TotalDuration : 0f;
            if (tracks == null)
            {
                return Mathf.Max(0.1f, endTime);
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                CombatTimelineClip[] clips = tracks[i]?.Clips;
                if (clips == null)
                {
                    continue;
                }

                for (int j = 0; j < clips.Length; j++)
                {
                    if (clips[j] != null)
                    {
                        endTime = Mathf.Max(endTime, GetClipDisplayEndTime(clips[j]));
                    }
                }
            }

            return Mathf.Max(0.1f, endTime);
        }
        /// <summary>
        /// 执行 Calculate / Timeline / Height 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float CalculateTimelineHeight(CombatTimelineTrack[] tracks)
        {
            float height = 0f;
            for (int i = 0; i < tracks.Length; i++)
            {
                height += CalculateTrackHeight(CalculateTrackLaneCount(tracks[i])) + 6f;
            }

            return height;
        }

        /// <summary>
        /// 执行 Calculate / Track / Height 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static float CalculateTrackHeight(int laneCount)
        {
            return TrackBasePadding + Mathf.Max(1, laneCount) * TrackLaneHeight;
        }

        /// <summary>
        /// 执行 Calculate / Track / Lane / Count 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private int CalculateTrackLaneCount(CombatTimelineTrack track)
        {
            if (track?.Clips == null || track.Clips.Length == 0)
            {
                return 1;
            }

            List<float> laneEnds = new List<float>();
            List<CombatTimelineClip> clips = SortedNonNullClips(track.Clips);
            for (int i = 0; i < clips.Count; i++)
            {
                CombatTimelineClip clip = clips[i];
                int lane = FindAvailableLane(laneEnds, clip.StartTime);
                if (lane < 0)
                {
                    laneEnds.Add(GetClipDisplayEndTime(clip));
                }
                else
                {
                    laneEnds[lane] = GetClipDisplayEndTime(clip);
                }
            }

            return Mathf.Max(1, laneEnds.Count);
        }

        /// <summary>
        /// 构建 Clip / Draw / Layouts 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private List<ClipDrawLayout> BuildClipDrawLayouts(CombatTimelineTrack track, CombatTimelineClip[] clips, Rect trackRect)
        {
            List<ClipDrawLayout> layouts = new List<ClipDrawLayout>();
            List<float> laneEnds = new List<float>();
            List<CombatTimelineClip> sortedClips = SortedNonNullClips(clips);
            for (int i = 0; i < sortedClips.Count; i++)
            {
                CombatTimelineClip clip = sortedClips[i];
                float displayEndTime = GetClipDisplayEndTime(clip);
                int lane = FindAvailableLane(laneEnds, clip.StartTime);
                if (lane < 0)
                {
                    lane = laneEnds.Count;
                    laneEnds.Add(displayEndTime);
                }
                else
                {
                    laneEnds[lane] = displayEndTime;
                }

                Rect rect = new Rect(
                    trackRect.x + clip.StartTime * pixelsPerSecond,
                    trackRect.y + 4f + lane * TrackLaneHeight,
                    Mathf.Max(8f, Mathf.Max(0f, displayEndTime - clip.StartTime) * pixelsPerSecond),
                    TrackLaneHeight - 5f);
                layouts.Add(new ClipDrawLayout(clip, track, rect));
            }

            return layouts;
        }

        /// <summary>
        /// 执行 Sorted / Non / Null / Clips 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static List<CombatTimelineClip> SortedNonNullClips(CombatTimelineClip[] clips)
        {
            List<CombatTimelineClip> sorted = new List<CombatTimelineClip>();
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                {
                    sorted.Add(clips[i]);
                }
            }

            sorted.Sort((left, right) =>
            {
                int start = left.StartTime.CompareTo(right.StartTime);
                if (start != 0)
                {
                    return start;
                }

                int end = left.EndTime.CompareTo(right.EndTime);
                if (end != 0)
                {
                    return end;
                }

                return Array.IndexOf(clips, left).CompareTo(Array.IndexOf(clips, right));
            });
            return sorted;
        }

        /// <summary>
        /// 查找 Available / Lane 对象或数据，作为后续绑定、校验或显示的输入。
        /// </summary>
        private static int FindAvailableLane(List<float> laneEnds, float startTime)
        {
            for (int i = 0; i < laneEnds.Count; i++)
            {
                if (startTime >= laneEnds[i] - 0.0001f)
                {
                    return i;
                }
            }

            return -1;
        }







        /// <summary>
        /// 绘制 Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawClip(CombatTimelineClip clip, Rect rect)
        {
            if (clip == null)
            {
                return;
            }

            if (clip is CombatAnimationClipWindow animation)
            {
                DrawAnimationPreviewClip(animation, rect);
            }
            else
            {
                Color fill = ClipColor(clip);
                EditorGUI.DrawRect(rect, fill);
            }
            DrawClipFrame(rect, clip == selectedClip);
            DrawClipDragHandles(clip, rect);

            Rect labelRect = new Rect(rect.x + 4f, rect.y + 2f, Mathf.Max(0f, rect.width - 8f), rect.height - 4f);
            GUI.Label(labelRect, clip.Name, EditorStyles.miniLabel);
        }

        /// <summary>
        /// 绘制 Animation / Preview / Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawAnimationPreviewClip(CombatAnimationClipWindow animation, Rect rect)
        {
            Color overflowColor = new Color(0.23f, 0.24f, 0.27f);
            Color timelineColor = new Color(0.16f, 0.43f, 0.86f);
            EditorGUI.DrawRect(rect, overflowColor);

            if (selectedTimeline == null)
            {
                return;
            }

            float visibleStart = Mathf.Max(animation.StartTime, 0f);
            float visibleEnd = Mathf.Min(GetClipDisplayEndTime(animation), selectedTimeline.TotalDuration);
            if (visibleEnd <= visibleStart)
            {
                return;
            }

            Rect validRect = new Rect(
                rect.x + (visibleStart - animation.StartTime) * pixelsPerSecond,
                rect.y,
                Mathf.Max(0f, (visibleEnd - visibleStart) * pixelsPerSecond),
                rect.height);
            EditorGUI.DrawRect(validRect, timelineColor);
        }
        /// <summary>
        /// 绘制 Clip / Frame 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private static void DrawClipFrame(Rect rect, bool selected)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, selected ? 0.75f : 0.28f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.55f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.7f));
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.7f));

            if (!selected)
            {
                return;
            }

            Color outline = Color.white;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 2f), outline);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), outline);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 2f, rect.height), outline);
            EditorGUI.DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), outline);
        }


        /// <summary>
        /// 绘制 Clip / Drag / Handles 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawClipDragHandles(CombatTimelineClip clip, Rect rect)
        {
            bool lengthLocked = IsLengthLockedClip(clip);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.MoveArrow);
            if (clip is CombatAnimationClipWindow)
            {
                EditorGUIUtility.AddCursorRect(GetAnimationSourceStartHandleRect(rect), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(GetAnimationSourceEndHandleRect(rect), MouseCursor.ResizeHorizontal);
                return;
            }

            if (!lengthLocked && rect.width >= 18f)
            {
                EditorGUIUtility.AddCursorRect(new Rect(rect.x, rect.y, ClipEdgeHandleWidth, rect.height), MouseCursor.ResizeHorizontal);
                EditorGUIUtility.AddCursorRect(new Rect(rect.xMax - ClipEdgeHandleWidth, rect.y, ClipEdgeHandleWidth, rect.height), MouseCursor.ResizeHorizontal);
            }
        }

        /// <summary>
        /// 处理 Timeline / Clip / Mouse 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
        private void HandleTimelineClipMouse()
        {
            Event current = Event.current;
            if (dragMode == TimelineDragMode.Playhead)
            {
                return;
            }

            if (current.type == EventType.MouseDown && current.button == 0)
            {
                ClipDrawLayout hit = HitTestClip(current.mousePosition);
                if (hit == null)
                {
                    return;
                }

                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
                selectedClip = hit.Clip;
                draggedClip = hit.Clip;
                dragStartMouseX = current.mousePosition.x;
                dragOriginalStartTime = hit.Clip.StartTime;
                dragOriginalEndTime = hit.Clip.EndTime;
                if (hit.Clip is CombatAnimationClipWindow animation)
                {
                    dragOriginalClipStartOffset = animation.ClipStartOffset;
                    dragOriginalClipEndOffset = GetAnimationSourceEndOffset(animation);
                }
                else
                {
                    dragOriginalClipStartOffset = 0f;
                    dragOriginalClipEndOffset = 0f;
                }
                dragMode = DetermineDragMode(hit, current.mousePosition);
                RecordTimelineUndo("Edit Combat Timeline Clip");
                Repaint();
                current.Use();
                return;
            }

            if (draggedClip == null)
            {
                return;
            }

            if (current.type == EventType.MouseDrag && current.button == 0 && dragMode != TimelineDragMode.None)
            {
                ApplyClipDrag(current.mousePosition.x);
                current.Use();
                return;
            }

            if (current.type == EventType.MouseUp && current.button == 0)
            {
                draggedClip = null;
                dragMode = TimelineDragMode.None;
                current.Use();
            }
        }

        /// <summary>
        /// 执行 Hit / Test / Clip 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private ClipDrawLayout HitTestClip(Vector2 mousePosition)
        {
            for (int i = visibleClipLayouts.Count - 1; i >= 0; i--)
            {
                ClipDrawLayout layout = visibleClipLayouts[i];
                if (layout.Rect.Contains(mousePosition))
                {
                    return layout;
                }
            }

            return null;
        }

        /// <summary>
        /// 执行 Determine / Drag / Mode 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static TimelineDragMode DetermineDragMode(ClipDrawLayout hit, Vector2 mousePosition)
        {
            CombatTimelineClip clip = hit.Clip;
            if (clip is CombatAnimationClipWindow)
            {
                if (GetAnimationSourceStartHandleRect(hit.Rect).Contains(mousePosition))
                {
                    return TimelineDragMode.AnimationSourceStart;
                }

                if (GetAnimationSourceEndHandleRect(hit.Rect).Contains(mousePosition))
                {
                    return TimelineDragMode.AnimationSourceEnd;
                }
            }

            bool lengthLocked = IsLengthLockedClip(clip);
            Rect leftEdge = new Rect(hit.Rect.x, hit.Rect.y, ClipEdgeHandleWidth, hit.Rect.height);
            Rect rightEdge = new Rect(hit.Rect.xMax - ClipEdgeHandleWidth, hit.Rect.y, ClipEdgeHandleWidth, hit.Rect.height);
            return !lengthLocked && leftEdge.Contains(mousePosition)
                ? TimelineDragMode.ResizeStart
                : !lengthLocked && rightEdge.Contains(mousePosition)
                    ? TimelineDragMode.ResizeEnd
                    : TimelineDragMode.Move;
        }

        /// <summary>
        /// 获取 Animation / Source / Start / Handle / Rect 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private static Rect GetAnimationSourceStartHandleRect(Rect rect)
        {
            return new Rect(rect.x, rect.y, Mathf.Min(AnimationSourceHandleWidth, rect.width * 0.5f), rect.height);
        }

        /// <summary>
        /// 获取 Animation / Source / End / Handle / Rect 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private static Rect GetAnimationSourceEndHandleRect(Rect rect)
        {
            float width = Mathf.Min(AnimationSourceHandleWidth, rect.width * 0.5f);
            return new Rect(rect.xMax - width, rect.y, width, rect.height);
        }

        /// <summary>
        /// 应用 Clip / Drag 结果到当前对象，可能改变资源、位移、动画或调试状态。
        /// </summary>
        private void ApplyClipDrag(float mouseX)
        {
            if (draggedClip == null || selectedTimeline == null)
            {
                return;
            }

            float deltaTime = (mouseX - dragStartMouseX) / Mathf.Max(1f, pixelsPerSecond);
            float originalDuration = Mathf.Max(0.001f, dragOriginalEndTime - dragOriginalStartTime);
            float minDuration = Mathf.Max(0.01f, 1f / Mathf.Max(1f, selectedTimeline.FrameRate));

            switch (dragMode)
            {
                case TimelineDragMode.Move:
                    if (draggedClip is CombatAnimationClipWindow animationMove)
                    {
                        float moveDuration = Mathf.Max(minDuration, GetAnimationSourceDuration(animationMove));
                        float movedStart = SnapTime(dragOriginalStartTime + deltaTime);
                        movedStart = Mathf.Clamp(movedStart, 0f, selectedTimeline.TotalDuration);
                        draggedClip.StartTime = movedStart;
                        draggedClip.EndTime = movedStart + moveDuration;
                    }
                    else
                    {
                        float movedStart = SnapTime(dragOriginalStartTime + deltaTime);
                        movedStart = Mathf.Clamp(movedStart, 0f, Mathf.Max(0f, selectedTimeline.TotalDuration - originalDuration));
                        draggedClip.StartTime = movedStart;
                        draggedClip.EndTime = Mathf.Min(selectedTimeline.TotalDuration, movedStart + originalDuration);
                    }
                    break;
                case TimelineDragMode.ResizeStart:
                    draggedClip.StartTime = SnapTime(Mathf.Clamp(dragOriginalStartTime + deltaTime, 0f, dragOriginalEndTime - minDuration));
                    draggedClip.EndTime = dragOriginalEndTime;
                    break;
                case TimelineDragMode.ResizeEnd:
                    draggedClip.StartTime = dragOriginalStartTime;
                    draggedClip.EndTime = SnapTime(Mathf.Clamp(dragOriginalEndTime + deltaTime, dragOriginalStartTime + minDuration, selectedTimeline.TotalDuration));
                    break;
                case TimelineDragMode.AnimationSourceStart:
                    if (draggedClip is CombatAnimationClipWindow animationStartOffset)
                    {
                        float sourceDelta = deltaTime * Mathf.Max(0.01f, animationStartOffset.ClipSpeed);
                        animationStartOffset.ClipStartOffset = SnapTime(ClampAnimationClipStartOffset(animationStartOffset, dragOriginalClipStartOffset + sourceDelta));
                        NormalizeAnimationWindowEnd(animationStartOffset);
                    }
                    break;
                case TimelineDragMode.AnimationSourceEnd:
                    if (draggedClip is CombatAnimationClipWindow animationEndOffset)
                    {
                        float sourceDelta = deltaTime * Mathf.Max(0.01f, animationEndOffset.ClipSpeed);
                        animationEndOffset.ClipEndOffset = SnapTime(ClampAnimationClipEndOffset(animationEndOffset, dragOriginalClipEndOffset + sourceDelta));
                        NormalizeAnimationWindowEnd(animationEndOffset);
                    }
                    break;
            }

            MarkTimelineChanged();
        }

        /// <summary>
        /// 绘制 Playhead 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawPlayhead(Rect area)
        {
            Rect ruler = GetRulerRect(area);
            float x = ruler.x + playheadTime * pixelsPerSecond;
            Rect line = new Rect(x - 1f, area.y, 2f, area.height);
            EditorGUI.DrawRect(line, Color.white);

            Handles.color = Color.white;
            Handles.DrawAAConvexPolygon(
                new Vector3(x - 5f, ruler.y),
                new Vector3(x + 5f, ruler.y),
                new Vector3(x, ruler.y + 8f));
        }

        /// <summary>
        /// 绘制 Inspector 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawInspector()
        {
            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);

            if (selectedTimeline == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawAssetInspector();
            GUILayout.Space(10f);
            DrawClipInspector();

            EditorGUILayout.EndScrollView();
        }

        /// <summary>
        /// 绘制 Asset / Inspector 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawAssetInspector()
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.ObjectField("Asset", selectedTimeline, typeof(CombatTimelineActionAsset), false);
            EditorGUILayout.EnumPopup("Owner", selectedTimeline.Owner);
            EditorGUILayout.EnumPopup("Kind", selectedTimeline.ActionKind);
            EditorGUILayout.TextField("Action Id", selectedTimeline.ActionId);
            EditorGUILayout.TextField("Display", selectedTimeline.DisplayName);
            EditorGUI.EndDisabledGroup();

            DrawTimelineFrameModelInspector();

            SerializedObject serialized = new SerializedObject(selectedTimeline);
            SerializedProperty iterator = serialized.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script" ||
                    iterator.name == "tracks" ||
                    iterator.name == "totalFrames" ||
                    iterator.name == "totalDuration" ||
                    iterator.name == "frameRate")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            if (serialized.ApplyModifiedProperties())
            {
                MarkTimelineChanged(false);
            }
        }

        /// <summary>
        /// 绘制 Timeline / Frame / Model / Inspector 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawTimelineFrameModelInspector()
        {
            EditorGUI.BeginChangeCheck();
            float nextFrameRate = EditorGUILayout.FloatField("Frame Rate", selectedTimeline.FrameRate);
            if (EditorGUI.EndChangeCheck())
            {
                RecordTimelineUndo("Edit Combat Timeline Frame Rate");
                selectedTimeline.SetFrameRatePreserveDuration(nextFrameRate);
                playheadTime = Mathf.Clamp(SnapToFrame(playheadTime), 0f, selectedTimeline.TotalDuration);
                MarkTimelineChanged(false);
            }

            EditorGUI.BeginChangeCheck();
            int nextTotalFrames = Mathf.Max(1, EditorGUILayout.IntField("Total Frames", selectedTimeline.TotalFrames));
            if (EditorGUI.EndChangeCheck())
            {
                RecordTimelineUndo("Edit Combat Timeline Total Frames");
                selectedTimeline.SetTotalFrames(nextTotalFrames);
                playheadTime = Mathf.Clamp(playheadTime, 0f, selectedTimeline.TotalDuration);
                MarkTimelineChanged(false);
            }

            EditorGUI.BeginChangeCheck();
            float nextDuration = EditorGUILayout.FloatField("Duration", selectedTimeline.TotalDuration);
            if (EditorGUI.EndChangeCheck())
            {
                RecordTimelineUndo("Edit Combat Timeline Duration");
                selectedTimeline.SetDurationFromSeconds(nextDuration);
                playheadTime = Mathf.Clamp(playheadTime, 0f, selectedTimeline.TotalDuration);
                MarkTimelineChanged(false);
            }

            EditorGUILayout.HelpBox("Duration is derived from Total Frames / Frame Rate. Changing Duration, Total Frames, or Frame Rate keeps clip Start/End seconds unchanged; displayed frame numbers are recalculated from the current Frame Rate.", MessageType.Info);
        }

        /// <summary>
        /// 绘制 Clip / Inspector 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawClipInspector()
        {
            if (selectedClip == null)
            {
                EditorGUILayout.HelpBox("Select a clip on the timeline.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Clip", EditorStyles.boldLabel);
            CombatTimelineClip beforeEdit = CloneClip(selectedClip, false);
            EditorGUI.BeginChangeCheck();
            selectedClip.Name = EditorGUILayout.TextField("Name", selectedClip.Name);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.EnumPopup("Type", selectedClip.ClipKind);
            EditorGUI.EndDisabledGroup();
            selectedClip.CapabilityId = (CombatTimelineCapabilityId)EditorGUILayout.EnumPopup("Capability", selectedClip.CapabilityId);
            DrawCapabilityInspector(selectedClip);
            if (selectedClip is CombatAnimationClipWindow animation)
            {
                float nextStart = Mathf.Clamp(TimeField("Start", selectedClip.StartTime), 0f, selectedTimeline.TotalDuration);
                selectedClip.StartTime = nextStart;
                NormalizeAnimationWindowEnd(animation);
                EditorGUI.BeginDisabledGroup(true);
                ReadOnlyTimeField("Duration", Mathf.Max(0f, GetClipDisplayEndTime(selectedClip) - selectedClip.StartTime));
                ReadOnlyTimeField("End", GetClipDisplayEndTime(selectedClip));
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                selectedClip.StartTime = TimeField("Start", selectedClip.StartTime);
                selectedClip.EndTime = Mathf.Max(selectedClip.StartTime, TimeField("End", selectedClip.EndTime));
            }

            if (selectedClip is CombatPhaseClip phaseClip)
            {
                GUILayout.Space(6f);
                phaseClip.Phase = (PlayerStatePhase)EditorGUILayout.EnumPopup("Phase", phaseClip.Phase);
            }
            else if (selectedClip is CombatHitNodeClip hit)
            {
                DrawHitClip(hit);
            }
            else if (selectedClip is CombatMotionReferenceClip motion)
            {
                DrawMotionReferenceClip(motion);
            }
            else if (selectedClip is CombatReactionWindowClip reaction)
            {
                DrawReactionClip(reaction);
            }
            else if (selectedClip is BossReactionGateWindowClip bossGate)
            {
                DrawBossReactionGateClip(bossGate);
            }
            else if (selectedClip is CombatAnimationClipWindow animationClip)
            {
                DrawAnimationClip(animationClip);
            }
            else if (selectedClip is CombatMarkerClip marker)
            {
                marker.MarkerId = EditorGUILayout.TextField("Marker Id", marker.MarkerId);
            }

            if (EditorGUI.EndChangeCheck())
            {
                CombatTimelineClip editedClip = CloneClip(selectedClip, false);
                ApplyClipValues(selectedClip, beforeEdit);
                RecordTimelineUndo("Edit Combat Timeline Clip Inspector");
                ApplyClipValues(selectedClip, editedClip);
                MarkTimelineChanged(false);
            }
        }


        /// <summary>
        /// 绘制窗口族能力摘要，帮助编辑器检查 CapabilityId 与窗口族是否一致。
        /// </summary>
        private static void DrawCapabilityInspector(CombatTimelineClip clip)
        {
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField("Capability", CombatTimelineCapabilityUtility.GetCapabilityLabel(clip));
            EditorGUI.EndDisabledGroup();

            if (!CombatTimelineCapabilityUtility.IsCapabilityCompatibleWithClip(clip))
            {
                EditorGUILayout.HelpBox("Capability does not match this window family. Choose a capability that belongs to this clip family.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox("Runtime reads explicit CapabilityId. CapabilityKind is only used for clip family grouping.", MessageType.Info);
            }
        }

        /// <summary>
        /// 绘制 Hit / Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawHitClip(CombatHitNodeClip hit)
        {
            GUILayout.Space(6f);
            hit.HitIndex = EditorGUILayout.IntField("Hit Index", hit.HitIndex);
            hit.SourcePart = (ProjectEVE.Boss.AI.BossAttackSourcePart)EditorGUILayout.EnumPopup("Source Part", hit.SourcePart);
            hit.ReactionIntent = (CombatReactionIntent)EditorGUILayout.EnumPopup("Reaction", hit.ReactionIntent);
            hit.AttackType = (CombatAttackType)EditorGUILayout.EnumPopup("Attack Type", hit.AttackType);
            hit.Damage = EditorGUILayout.FloatField("Damage", hit.Damage);
            hit.PoiseDamage = EditorGUILayout.FloatField("Poise Damage", hit.PoiseDamage);
            hit.GuardDamage = EditorGUILayout.FloatField("Guard Damage", hit.GuardDamage);
            hit.CanBeGuarded = EditorGUILayout.Toggle("Can Guard", hit.CanBeGuarded);
            hit.CanBePerfectGuarded = EditorGUILayout.Toggle("Can Perfect Guard", hit.CanBePerfectGuarded);
            hit.CanBePerfectEvaded = EditorGUILayout.Toggle("Can Perfect Evade", hit.CanBePerfectEvaded);
            hit.MaxHitsPerTarget = EditorGUILayout.IntField("Max Hits", hit.MaxHitsPerTarget);
            hit.EffectiveRange = EditorGUILayout.FloatField("Effective Range", hit.EffectiveRange);
            hit.EffectiveAngle = EditorGUILayout.FloatField("Effective Angle", hit.EffectiveAngle);
            hit.TriggersPerfectGuardBossStagger = EditorGUILayout.Toggle("PG Stagger Boss", hit.TriggersPerfectGuardBossStagger);
        }

        /// <summary>
        /// 绘制 Motion / Reference / Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawMotionReferenceClip(CombatMotionReferenceClip motion)
        {
            GUILayout.Space(6f);
            EditorGUILayout.HelpBox("Motion values are read from MotionProfile assets. Timeline stores only references for visualization.", MessageType.Info);
            motion.MotionKind = (CombatTimelineClipKind)EditorGUILayout.EnumPopup("Motion Kind", motion.MotionKind);
            motion.PlayerMotionId = (ProjectEVE.Player.Movement.PlayerActionMotionId)EditorGUILayout.EnumPopup("Player Motion Id", motion.PlayerMotionId);
            motion.BossAttackId = EditorGUILayout.TextField("Boss Attack Id", motion.BossAttackId);
            motion.ProfileWindowName = EditorGUILayout.TextField("Profile Window", motion.ProfileWindowName);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Player Profile"))
            {
                PingFirstAsset("t:PlayerActionMotionProfile");
            }

            if (GUILayout.Button("Select Boss Profile"))
            {
                PingFirstAsset("t:BossMotionWarpProfile");
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制 Reaction / Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawReactionClip(CombatReactionWindowClip reaction)
        {
            GUILayout.Space(6f);
            reaction.ReactionType = (CombatTimelineReactionType)EditorGUILayout.EnumPopup("Reaction Type", reaction.ReactionType);
            reaction.Direction = (CombatTimelineReactionDirection)EditorGUILayout.EnumPopup("Direction", reaction.Direction);
            reaction.StunDuration = EditorGUILayout.FloatField("Stun Duration", reaction.StunDuration);
            reaction.RecoveryStartTime = EditorGUILayout.FloatField("Recovery Start", reaction.RecoveryStartTime);
            reaction.CanReturnTime = EditorGUILayout.FloatField("Can Return", reaction.CanReturnTime);
            reaction.ReactionMotionId = (ProjectEVE.Player.Movement.PlayerActionMotionId)EditorGUILayout.EnumPopup("Motion Id", reaction.ReactionMotionId);
        }

        private static void DrawBossReactionGateClip(BossReactionGateWindowClip gate)
        {
            GUILayout.Space(6f);
            gate.Policy = (BossReactionGatePolicy)EditorGUILayout.EnumPopup("Policy", gate.Policy);
            gate.BlockedOutcome = (BossReactionGateBlockedOutcome)EditorGUILayout.EnumPopup("Blocked Outcome", gate.BlockedOutcome);
            EditorGUILayout.LabelField("Attack Types", EditorStyles.boldLabel);
            gate.AttackTypes = DrawAttackTypeList(gate.AttackTypes);
        }

        private static CombatAttackType[] DrawAttackTypeList(CombatAttackType[] values)
        {
            Array enumValues = Enum.GetValues(typeof(CombatAttackType));
            List<CombatAttackType> selected = new List<CombatAttackType>(values ?? Array.Empty<CombatAttackType>());
            for (int i = 0; i < enumValues.Length; i++)
            {
                CombatAttackType attackType = (CombatAttackType)enumValues.GetValue(i);
                bool enabled = selected.Contains(attackType);
                bool nextEnabled = EditorGUILayout.Toggle(attackType.ToString(), enabled);
                if (nextEnabled == enabled)
                {
                    continue;
                }

                if (nextEnabled)
                {
                    selected.Add(attackType);
                }
                else
                {
                    selected.Remove(attackType);
                }
            }

            return selected.ToArray();
        }

        /// <summary>
        /// 绘制 Animation / Clip 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawAnimationClip(CombatAnimationClipWindow animation)
        {
            GUILayout.Space(6f);
            AnimationClip previousPreviewClip = animation.PreviewAnimationClip;
            animation.PreviewAnimationClip = (AnimationClip)EditorGUILayout.ObjectField("Preview Clip", animation.PreviewAnimationClip, typeof(AnimationClip), false);
            if (animation.PreviewAnimationClip != previousPreviewClip && animation.PreviewAnimationClip != null)
            {
                animation.ClipStartOffset = 0f;
                animation.ClipEndOffset = animation.PreviewAnimationClip.length;
            }

            animation.AnimatorStateName = EditorGUILayout.TextField("Animator State", animation.AnimatorStateName);
            NormalizeAnimationSourceOffsets(animation);
            animation.ClipStartOffset = SnapTime(ClampAnimationClipStartOffset(animation, TimeField("Clip Start Offset", animation.ClipStartOffset)));
            animation.ClipEndOffset = SnapTime(ClampAnimationClipEndOffset(animation, TimeField("Clip End Offset", GetAnimationSourceEndOffset(animation))));
            animation.ClipSpeed = EditorGUILayout.FloatField("Clip Speed", Mathf.Max(0.01f, animation.ClipSpeed));
            animation.LoopPreview = EditorGUILayout.Toggle("Loop Preview", animation.LoopPreview);
            NormalizeAnimationWindowEnd(animation);

            bool active = IsAnimationPreviewWindowActive(animation);
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.Toggle("Preview Active", active);
            ReadOnlyTimeField("Timeline Local Time", GetAnimationWindowLocalTime(animation));
            float sampleTime = animation.PreviewAnimationClip != null ? CalculateAnimationSampleTime(animation, animation.PreviewAnimationClip) : 0f;
            ReadOnlyTimeField("Sample Time", sampleTime);
            ReadOnlyTimeField("Source Duration", Mathf.Max(0f, GetAnimationSourceEndOffset(animation) - animation.ClipStartOffset));
            ReadOnlyTimeField("Timeline Duration", Mathf.Max(0f, GetClipDisplayEndTime(animation) - animation.StartTime));
            ReadOnlyTimeField("Clip Length", animation.PreviewAnimationClip != null ? animation.PreviewAnimationClip.length : 0f);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(animation.PreviewAnimationClip == null);
            if (GUILayout.Button("Use Full Clip"))
            {
                animation.ClipStartOffset = 0f;
                animation.ClipEndOffset = animation.PreviewAnimationClip != null ? animation.PreviewAnimationClip.length : 0f;
                NormalizeAnimationWindowEnd(animation);
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Fit To Timeline Remaining"))
            {
                float timelineRemaining = Mathf.Max(1f / GetFrameRate(), selectedTimeline.TotalDuration - animation.StartTime);
                float sourceRemaining = Mathf.Max(1f / GetFrameRate(), GetAnimationSourceEndOffset(animation) - animation.ClipStartOffset);
                animation.ClipSpeed = Mathf.Max(0.01f, sourceRemaining / timelineRemaining);
                NormalizeAnimationWindowEnd(animation);
            }
            EditorGUILayout.EndHorizontal();

            if (!active)
            {
                EditorGUILayout.HelpBox("Selected Animation Preview clips are sampled first, even when the playhead is outside this window. Without a selected Animation Preview, Edit Preview uses the last sampled clip, then the topmost visible clip.", MessageType.Info);
            }
        }

        /// <summary>
        /// 判断当前对象是否处于 Animation / Preview / Window / Active 状态，避免调用方直接读取内部实现细节。
        /// </summary>
        private bool IsAnimationPreviewWindowActive(CombatAnimationClipWindow animation)
        {
            return playheadTime >= animation.StartTime - 0.0001f && playheadTime <= GetClipDisplayEndTime(animation) + 0.0001f;
        }

        /// <summary>
        /// 获取 Animation / Window / Local / Time 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetAnimationWindowLocalTime(CombatAnimationClipWindow animation)
        {
            return Mathf.Max(0f, playheadTime - animation.StartTime);
        }



        /// <summary>
        /// 获取 Clip / Display / End / Time 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetClipDisplayEndTime(CombatTimelineClip clip)
        {
            if (clip is CombatAnimationClipWindow animation)
            {
                return animation.StartTime + GetAnimationSourceDuration(animation);
            }

            return clip.EndTime;
        }

        /// <summary>
        /// 获取 Animation / Source / Duration 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetAnimationSourceDuration(CombatAnimationClipWindow animation)
        {
            float minDuration = 1f / GetFrameRate();
            if (animation == null)
            {
                return minDuration;
            }

            float speed = Mathf.Max(0.01f, animation.ClipSpeed);
            if (animation.PreviewAnimationClip == null || animation.PreviewAnimationClip.length <= 0.001f)
            {
                return Mathf.Max(minDuration, animation.Duration);
            }

            NormalizeAnimationSourceOffsets(animation);
            float sourceDuration = Mathf.Max(0f, GetAnimationSourceEndOffset(animation) - animation.ClipStartOffset);
            return Mathf.Max(minDuration, sourceDuration / speed);
        }

        /// <summary>
        /// 获取 Animation / Source / End / Offset 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetAnimationSourceEndOffset(CombatAnimationClipWindow animation)
        {
            if (animation?.PreviewAnimationClip == null || animation.PreviewAnimationClip.length <= 0.001f)
            {
                return animation == null ? 0f : Mathf.Max(animation.ClipEndOffset, animation.EndTime - animation.StartTime);
            }

            if (animation.ClipEndOffset <= animation.ClipStartOffset + 0.0001f)
            {
                return animation.PreviewAnimationClip.length;
            }

            return Mathf.Clamp(animation.ClipEndOffset, 0f, animation.PreviewAnimationClip.length);
        }

        /// <summary>
        /// 执行 Clamp / Animation / Clip / Start / Offset 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float ClampAnimationClipStartOffset(CombatAnimationClipWindow animation, float value)
        {
            if (animation == null)
            {
                return Mathf.Max(0f, value);
            }

            float minSourceDuration = (1f / GetFrameRate()) * Mathf.Max(0.01f, animation.ClipSpeed);
            float sourceEnd = GetAnimationSourceEndOffset(animation);
            if (animation.PreviewAnimationClip == null || animation.PreviewAnimationClip.length <= 0.001f)
            {
                return Mathf.Clamp(value, 0f, Mathf.Max(0f, sourceEnd - minSourceDuration));
            }

            return Mathf.Clamp(value, 0f, Mathf.Max(0f, sourceEnd - minSourceDuration));
        }

        /// <summary>
        /// 执行 Clamp / Animation / Clip / End / Offset 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float ClampAnimationClipEndOffset(CombatAnimationClipWindow animation, float value)
        {
            if (animation == null)
            {
                return Mathf.Max(0f, value);
            }

            float minSourceDuration = (1f / GetFrameRate()) * Mathf.Max(0.01f, animation.ClipSpeed);
            float minEnd = animation.ClipStartOffset + minSourceDuration;
            if (animation.PreviewAnimationClip == null || animation.PreviewAnimationClip.length <= 0.001f)
            {
                return Mathf.Max(minEnd, value);
            }

            return Mathf.Clamp(value, minEnd, animation.PreviewAnimationClip.length);
        }

        /// <summary>
        /// 执行 Normalize / Animation / Source / Offsets 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void NormalizeAnimationSourceOffsets(CombatAnimationClipWindow animation)
        {
            if (animation == null)
            {
                return;
            }

            animation.ClipSpeed = Mathf.Max(0.01f, animation.ClipSpeed);
            float sourceEnd = GetAnimationSourceEndOffset(animation);
            animation.ClipStartOffset = ClampAnimationClipStartOffset(animation, animation.ClipStartOffset);
            animation.ClipEndOffset = ClampAnimationClipEndOffset(animation, sourceEnd);
        }

        /// <summary>
        /// 执行 Normalize / Animation / Window / End 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void NormalizeAnimationWindowEnd(CombatAnimationClipWindow animation)
        {
            if (animation == null || selectedTimeline == null)
            {
                return;
            }

            animation.StartTime = Mathf.Clamp(animation.StartTime, 0f, selectedTimeline.TotalDuration);
            NormalizeAnimationSourceOffsets(animation);
            animation.EndTime = animation.StartTime + GetAnimationSourceDuration(animation);
        }
        /// <summary>
        /// 执行 Time / Field 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float TimeField(string label, float value)
        {
            if (timeDisplayMode == TimelineTimeDisplayMode.Frames)
            {
                int frame = EditorGUILayout.IntField(label, TimeToFrame(value));
                value = FrameToTime(frame);
            }
            else
            {
                value = EditorGUILayout.FloatField(label, value);
            }

            if (snapToFrame)
            {
                value = SnapToFrame(value);
            }

            return Mathf.Max(0f, value);
        }

        /// <summary>
        /// 执行 Read / Only / Time / Field 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void ReadOnlyTimeField(string label, float value)
        {
            if (timeDisplayMode == TimelineTimeDisplayMode.Frames)
            {
                EditorGUILayout.IntField(label, TimeToFrame(value));
            }
            else
            {
                EditorGUILayout.FloatField(label, value);
            }
        }


        /// <summary>
        /// 执行 Snap / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float SnapTime(float value)
        {
            return snapToFrame ? SnapToFrame(value) : Mathf.Max(0f, value);
        }

        /// <summary>
        /// 执行 Snap / To / Frame 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float SnapToFrame(float value)
        {
            float frameRate = GetFrameRate();
            return Mathf.Max(0f, Mathf.Round(value * frameRate) / frameRate);
        }

        /// <summary>
        /// 执行 Time / To / Frame 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private int TimeToFrame(float time)
        {
            return Mathf.RoundToInt(Mathf.Max(0f, time) * GetFrameRate());
        }

        /// <summary>
        /// 执行 Frame / To / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float FrameToTime(int frame)
        {
            return Mathf.Max(0, frame) / GetFrameRate();
        }

        /// <summary>
        /// 获取 Frame / Rate 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private float GetFrameRate()
        {
            return Mathf.Max(1f, selectedTimeline != null ? selectedTimeline.FrameRate : 60f);
        }

        /// <summary>
        /// 执行 Format / Timeline / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private string FormatTimelineTime(float time)
        {
            return timeDisplayMode == TimelineTimeDisplayMode.Frames ? $"{TimeToFrame(time)}f" : $"{time:0.00}s";
        }

        /// <summary>
        /// 获取 Ruler / Rect 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private static Rect GetRulerRect(Rect area)
        {
            return new Rect(area.x + 110f, area.y, area.width - 120f, 22f);
        }

        /// <summary>
        /// 执行 Calculate / Frame / Label / Step 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static int CalculateFrameLabelStep(float framePixelWidth)
        {
            if (framePixelWidth >= 18f)
            {
                return 1;
            }

            if (framePixelWidth >= 8f)
            {
                return 5;
            }

            if (framePixelWidth >= 4f)
            {
                return 10;
            }

            return 30;
        }


        /// <summary>
        /// 判断当前对象是否处于 Length / Locked / Clip 状态，避免调用方直接读取内部实现细节。
        /// </summary>
        private static bool IsLengthLockedClip(CombatTimelineClip clip)
        {
            return clip is CombatAnimationClipWindow;
        }

        /// <summary>
        /// 绘制 Validation 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawValidation()
        {
            if (Event.current.type == EventType.Layout)
            {
                RefreshValidationGuiSnapshot();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Run", GUILayout.Width(70f)))
            {
                ValidateSelected();
            }

            EditorGUILayout.EndHorizontal();
            validationScroll = EditorGUILayout.BeginScrollView(validationScroll);
            for (int i = 0; i < validationGuiMessages.Count; i++)
            {
                DrawValidationMessage(validationGuiMessages[i], validationGuiMessageTypes[i]);
            }

            EditorGUILayout.EndScrollView();
        }

        /// <summary>在 Layout 事件冻结本轮 Validation 绘制内容，避免数据在 Repaint 前变化导致 GUILayout 控件数量不一致。</summary>
        private void RefreshValidationGuiSnapshot()
        {
            validationGuiMessages.Clear();
            validationGuiMessageTypes.Clear();
            for (int i = 0; i < validationMessages.Count; i++)
            {
                CombatTimelineValidationMessage message = validationMessages[i];
                validationGuiMessages.Add(message.Message);
                validationGuiMessageTypes.Add(message.Severity == CombatTimelineValidationSeverity.Error
                    ? MessageType.Error
                    : message.Severity == CombatTimelineValidationSeverity.Warning ? MessageType.Warning : MessageType.Info);
            }

            if (editModePreview && !string.IsNullOrEmpty(previewStatusMessage))
            {
                validationGuiMessages.Add(previewStatusMessage);
                validationGuiMessageTypes.Add(previewStatusType);
            }

            if (TimelineHasOverlappingClips(selectedTimeline))
            {
                validationGuiMessages.Add("Some clips overlap on the same track. They are automatically split into visual lanes; this does not change timeline data.");
                validationGuiMessageTypes.Add(MessageType.Info);
            }
        }

        private static void DrawValidationMessage(string message, MessageType type)
        {
            string prefix = type == MessageType.Error ? "Error" : type == MessageType.Warning ? "Warning" : "Info";
            GUILayout.Label($"{prefix}: {message}", EditorStyles.helpBox);
        }

        /// <summary>
        /// 执行 Timeline / Has / Overlapping / Clips 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private bool TimelineHasOverlappingClips(CombatTimelineActionAsset timeline)
        {
            if (timeline?.Tracks == null)
            {
                return false;
            }

            for (int i = 0; i < timeline.Tracks.Length; i++)
            {
                if (CalculateTrackLaneCount(timeline.Tracks[i]) > 1)
                {
                    return true;
                }
            }

            return false;
        }


        /// <summary>
        /// 刷新 Timelines 数据，使显示、缓存或运行时状态与当前配置保持一致。
        /// </summary>
        private void RefreshTimelines()
        {
            timelines.Clear();
            HashSet<string> guids = new HashSet<string>();
            AddTimelineAssetGuids(guids);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CombatTimelineActionAsset timeline = AssetDatabase.LoadAssetAtPath<CombatTimelineActionAsset>(path);
                if (timeline != null)
                {
                    timelines.Add(timeline);
                }
            }

            timelines.Sort((left, right) =>
            {
                int kind = left.ActionKind.CompareTo(right.ActionKind);
                return kind != 0 ? kind : string.Compare(left.ActionId, right.ActionId, StringComparison.Ordinal);
            });

            if (selectedTimeline == null && timelines.Count > 0)
            {
                SelectTimeline(timelines[0]);
            }
            else
            {
                ClearInvalidAnimationPreviewCache();
                ValidateSelected();
            }

            Repaint();
        }

        /// <summary>
        /// 手动刷新 Combat Timeline 运行时缓存，用于 Play Mode 中应用已保存的 Timeline 资产改动。
        /// </summary>
        private void RefreshRuntimeCache()
        {
            AssetDatabase.SaveAssets();
            CombatTimelineProvider.ResetRuntimeDataCache();
            CombatTimelineProvider.RebuildRuntimeDataCache();
            ValidateSelected();
            ShowNotification(new GUIContent("Runtime cache refreshed"));
            Repaint();
        }

        /// <summary>
        /// 添加 Timeline / Asset / Guids 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddTimelineAssetGuids(HashSet<string> destination)
        {
            AddAssetGuids("t:PlayerAttackTimelineAsset", destination);
            AddAssetGuids("t:PlayerSkillTimelineAsset", destination);
            AddAssetGuids("t:PlayerEvadeTimelineAsset", destination);
            AddAssetGuids("t:PlayerGuardTimelineAsset", destination);
            AddAssetGuids("t:PlayerReactionTimelineAsset", destination);
            AddAssetGuids("t:BossAttackTimelineAsset", destination);
            AddAssetGuids("t:BossReactionTimelineAsset", destination);
        }

        /// <summary>
        /// 添加 Asset / Guids 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddAssetGuids(string filter, HashSet<string> destination)
        {
            string[] foundGuids = AssetDatabase.FindAssets(filter, new[] { "Assets/ScriptableObjects/CombatTimelines" });
            for (int i = 0; i < foundGuids.Length; i++)
            {
                destination.Add(foundGuids[i]);
            }
        }

        /// <summary>
        /// 执行 Select / Timeline 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void SelectTimeline(CombatTimelineActionAsset timeline)
        {
            if (selectedTimeline != timeline && editModePreview)
            {
                StopPreview();
            }
            else if (selectedTimeline != timeline)
            {
                lastSampledAnimationPreviewClip = null;
            }

            selectedTimeline = timeline;
            selectedClip = null;
            playheadTime = 0f;
            ValidateSelected();
        }

        /// <summary>
        /// 校验 Selected 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private void ValidateSelected()
        {
            validationMessages.Clear();
            if (selectedTimeline != null)
            {
                validationMessages.AddRange(CombatTimelineValidator.Validate(selectedTimeline));
            }
        }

        /// <summary>
        /// 处理 Timeline / Keys 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
        private void HandleTimelineKeys()
        {
            Event current = Event.current;
            if (current.type != EventType.KeyDown)
            {
                return;
            }

            if (ShouldLeaveKeyToTextField(current))
            {
                return;
            }

            if (current.keyCode == KeyCode.Delete && selectedClip != null)
            {
                DeleteSelectedClip();
                current.Use();
            }
            else if (IsActionKeyPressed(current) && current.keyCode == KeyCode.C && selectedClip != null)
            {
                clipboardClip = CloneClip(selectedClip);
                current.Use();
            }
            else if (IsActionKeyPressed(current) && current.keyCode == KeyCode.V && clipboardClip != null)
            {
                PasteClip();
                current.Use();
            }
        }

        /// <summary>
        /// 判断键盘事件是否应交还给当前文本控件，避免 Inspector 粘贴误触 Timeline Clip 粘贴。
        /// </summary>
        private static bool ShouldLeaveKeyToTextField(Event current)
        {
            bool hasFocusedTextControl = EditorGUIUtility.editingTextField || GUIUtility.keyboardControl != 0;
            if (!hasFocusedTextControl)
            {
                return false;
            }

            if (current.keyCode == KeyCode.Delete || current.keyCode == KeyCode.Backspace)
            {
                return true;
            }

            if (!IsActionKeyPressed(current))
            {
                return false;
            }

            return current.keyCode == KeyCode.C ||
                   current.keyCode == KeyCode.V ||
                   current.keyCode == KeyCode.X ||
                   current.keyCode == KeyCode.A;
        }

        private static bool IsActionKeyPressed(Event current)
        {
            return current.control || current.command;
        }

        /// <summary>
        /// 执行 Delete / Selected / Clip 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void DeleteSelectedClip()
        {
            foreach (CombatTimelineTrack track in selectedTimeline.Tracks ?? Array.Empty<CombatTimelineTrack>())
            {
                if (track?.Clips == null)
                {
                    continue;
                }

                List<CombatTimelineClip> clips = new List<CombatTimelineClip>(track.Clips);
                if (clips.Remove(selectedClip))
                {
                    RecordTimelineUndo("Delete Combat Timeline Clip");
                    if (ReferenceEquals(selectedClip, lastSampledAnimationPreviewClip))
                    {
                        lastSampledAnimationPreviewClip = null;
                    }

                    track.Clips = clips.ToArray();
                    selectedClip = null;
                    MarkTimelineChanged();
                    return;
                }
            }
        }

        /// <summary>
        /// 执行 Paste / Clip 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void PasteClip()
        {
            CombatTimelineTrack[] tracks = selectedTimeline.Tracks;
            if (tracks == null || tracks.Length == 0)
            {
                return;
            }

            CombatTimelineClip clone = CloneClip(clipboardClip);
            clone.StartTime = Mathf.Clamp(playheadTime, 0f, selectedTimeline.TotalDuration);
            if (clone is CombatAnimationClipWindow animationClone)
            {
                NormalizeAnimationWindowEnd(animationClone);
            }
            else
            {
                clone.EndTime = Mathf.Min(selectedTimeline.TotalDuration, clone.StartTime + clipboardClip.Duration);
            }
            RecordTimelineUndo("Paste Combat Timeline Clip");
            List<CombatTimelineClip> clips = new List<CombatTimelineClip>(tracks[0].Clips ?? Array.Empty<CombatTimelineClip>()) { clone };
            tracks[0].Clips = clips.ToArray();
            selectedClip = clone;
            MarkTimelineChanged();
        }

        /// <summary>
        /// 执行 Sample / Preview 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private void SamplePreview()
        {
            previewStatusMessage = null;
            CombatAnimationClipWindow animationWindow = ResolveAnimationPreviewWindowForPlayback();
            if (animationWindow != null && animationWindow.PreviewAnimationClip == null)
            {
                previewStatusType = MessageType.Warning;
                previewStatusMessage = "Edit Preview: selected Animation Preview clip has no Preview Clip assigned.";
                return;
            }

            if (animationWindow == null || selectedTimeline == null)
            {
                previewStatusType = MessageType.Warning;
                previewStatusMessage = "Edit Preview: no Animation Preview clip is available for the current timeline.";
                return;
            }

            AnimationClip clip = animationWindow.PreviewAnimationClip;

            if (previewTarget == null)
            {
                previewTarget = ResolvePreviewTarget();
            }

            if (previewTarget == null)
            {
                previewStatusType = MessageType.Info;
                previewStatusMessage = "Edit Preview: no scene preview target was found.";
                return;
            }

            if (!AnimationMode.InAnimationMode())
            {
                AnimationMode.StartAnimationMode();
            }

            float sampleTime = CalculateAnimationSampleTime(animationWindow, clip);

            AnimationMode.SampleAnimationClip(previewTarget, clip, sampleTime);
            lastSampledAnimationPreviewClip = animationWindow;

            SceneView.RepaintAll();
        }

        /// <summary>
        /// 解析 Active / Animation / Preview / Window 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private CombatAnimationClipWindow ResolveAnimationPreviewWindowForPlayback()
        {
            if (selectedTimeline == null)
            {
                lastSampledAnimationPreviewClip = null;
                return null;
            }

            if (selectedClip is CombatAnimationClipWindow selectedAnimation)
            {
                return selectedAnimation;
            }

            ClearInvalidAnimationPreviewCache();
            if (lastSampledAnimationPreviewClip != null)
            {
                return lastSampledAnimationPreviewClip;
            }

            return ResolveTopmostAnimationPreviewWindow() ?? ResolveFirstAnimationPreviewWindow();
        }

        /// <summary>
        /// 解析 Topmost / Animation / Preview / Window 结果，并保持预览选择与可点击层级一致。
        /// </summary>
        private CombatAnimationClipWindow ResolveTopmostAnimationPreviewWindow()
        {
            for (int i = visibleClipLayouts.Count - 1; i >= 0; i--)
            {
                if (visibleClipLayouts[i].Clip is CombatAnimationClipWindow animationWindow && IsValidAnimationPreviewWindow(animationWindow))
                {
                    return animationWindow;
                }
            }

            return null;
        }

        /// <summary>
        /// 解析 First / Animation / Preview / Window 结果，作为可见布局尚未构建时的稳定兜底。
        /// </summary>
        private CombatAnimationClipWindow ResolveFirstAnimationPreviewWindow()
        {
            foreach (CombatAnimationClipWindow animationWindow in selectedTimeline.EnumerateClips<CombatAnimationClipWindow>())
            {
                if (IsValidAnimationPreviewWindow(animationWindow))
                {
                    return animationWindow;
                }
            }

            return null;
        }

        /// <summary>
        /// 清理 Animation / Preview / Cache 数据，避免删除或切换 Timeline 后继续采样悬挂引用。
        /// </summary>
        private void ClearInvalidAnimationPreviewCache()
        {
            if (!IsValidAnimationPreviewWindow(lastSampledAnimationPreviewClip))
            {
                lastSampledAnimationPreviewClip = null;
                return;
            }

            if (!SelectedTimelineContainsClip(lastSampledAnimationPreviewClip))
            {
                lastSampledAnimationPreviewClip = null;
            }
        }

        /// <summary>
        /// 判断 Animation / Preview / Window 是否可用于实际采样。
        /// </summary>
        private bool IsValidAnimationPreviewWindow(CombatAnimationClipWindow animationWindow)
        {
            return animationWindow != null
                && animationWindow.PreviewAnimationClip != null
                && SelectedTimelineContainsClip(animationWindow);
        }

        /// <summary>
        /// 判断当前 Timeline 是否仍持有指定 Clip，避免使用已删除或其他资产上的引用。
        /// </summary>
        private bool SelectedTimelineContainsClip(CombatTimelineClip targetClip)
        {
            if (selectedTimeline == null || targetClip == null)
            {
                return false;
            }

            foreach (CombatTimelineClip clip in selectedTimeline.EnumerateAllClips())
            {
                if (ReferenceEquals(clip, targetClip))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行 Calculate / Animation / Sample / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private float CalculateAnimationSampleTime(CombatAnimationClipWindow animationWindow, AnimationClip clip)
        {
            float localTime = Mathf.Max(0f, playheadTime - animationWindow.StartTime);
            float sourceStart = animationWindow.ClipStartOffset;
            float sourceEnd = GetAnimationSourceEndOffset(animationWindow);
            float sampleTime = sourceStart + localTime * Mathf.Max(0.01f, animationWindow.ClipSpeed);
            return ClampOrLoopSampleTime(sampleTime, clip, animationWindow.LoopPreview, sourceStart, sourceEnd);
        }

        /// <summary>
        /// 执行 Clamp / Or / Loop / Sample / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static float ClampOrLoopSampleTime(float sampleTime, AnimationClip clip, bool loop)
        {
            if (clip == null || clip.length <= 0.001f)
            {
                return Mathf.Max(0f, sampleTime);
            }

            return loop ? Mathf.Repeat(sampleTime, clip.length) : Mathf.Clamp(sampleTime, 0f, clip.length);
        }

        /// <summary>
        /// 执行 Clamp / Or / Loop / Sample / Time 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static float ClampOrLoopSampleTime(float sampleTime, AnimationClip clip, bool loop, float sourceStart, float sourceEnd)
        {
            if (clip == null || clip.length <= 0.001f)
            {
                return Mathf.Max(0f, sampleTime);
            }

            sourceStart = Mathf.Clamp(sourceStart, 0f, clip.length);
            sourceEnd = Mathf.Clamp(sourceEnd, sourceStart, clip.length);
            float sourceDuration = Mathf.Max(0.001f, sourceEnd - sourceStart);
            return loop
                ? sourceStart + Mathf.Repeat(sampleTime - sourceStart, sourceDuration)
                : Mathf.Clamp(sampleTime, sourceStart, sourceEnd);
        }



        /// <summary>
        /// 解析 Preview / Target 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private GameObject ResolvePreviewTarget()
        {
            if (selectedTimeline == null)
            {
                return null;
            }

            string targetName = selectedTimeline.Owner == CombatTimelineOwner.Player ? "CH_P_EVE_51" : "RavenMonster_Mesh";
            GameObject found = GameObject.Find(targetName);
            if (found != null)
            {
                Animator childAnimator = found.GetComponentInChildren<Animator>();
                return childAnimator != null ? childAnimator.gameObject : found;
            }

            Animator animator = FindObjectOfType<Animator>();
            return animator != null ? animator.gameObject : null;
        }

        /// <summary>
        /// 停止 Preview 流程，并清理当前动作、位移或显示状态。
        /// </summary>
        private void StopPreview()
        {
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }

            previewTarget = null;
            lastSampledAnimationPreviewClip = null;
        }

        /// <summary>
        /// 执行 Ping / First / Asset 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static void PingFirstAsset(string filter)
        {
            string[] guids = AssetDatabase.FindAssets(filter, new[] { "Assets" });
            if (guids.Length == 0)
            {
                return;
            }

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        /// <summary>
        /// 执行 Clone / Clip 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatTimelineClip CloneClip(CombatTimelineClip source, bool appendCopySuffix = true)
        {
            CombatTimelineClip clone = source switch
            {
                CombatPhaseClip value => CopyPhase(value),
                CombatAttackWindowClip value => CopyBase(value, new CombatAttackWindowClip()),
                CombatCancelWindowClip value => CopyBase(value, new CombatCancelWindowClip()),
                CombatDefenseWindowClip value => CopyBase(value, new CombatDefenseWindowClip()),
                CombatArmorWindowClip value => CopyBase(value, new CombatArmorWindowClip()),
                CombatHitNodeClip value => CopyHit(value),
                CombatReactionWindowClip value => CopyReaction(value),
                BossReactionGateWindowClip value => CopyBossReactionGate(value),
                CombatMotionReferenceClip value => CopyMotion(value),
                CombatAnimationClipWindow value => CopyAnimation(value),
                CombatMarkerClip value => CopyMarker(value),
                _ => CopyBase(source, new CombatMarkerClip())
            };

            if (appendCopySuffix)
            {
                clone.Name = $"{source.Name}_Copy";
            }

            return clone;
        }

        /// <summary>
        /// 把一个 Clip 快照写回现有 Clip 实例，用于在 Inspector 修改前后建立正确 Undo 边界。
        /// </summary>
        private static void ApplyClipValues(CombatTimelineClip target, CombatTimelineClip source)
        {
            if (target == null || source == null || target.GetType() != source.GetType())
            {
                return;
            }

            CopyBaseValues(source, target);
            switch (target)
            {
                case CombatPhaseClip targetPhase when source is CombatPhaseClip sourcePhase:
                    targetPhase.Phase = sourcePhase.Phase;
                    break;
                case CombatHitNodeClip targetHit when source is CombatHitNodeClip sourceHit:
                    targetHit.AttackType = sourceHit.AttackType;
                    targetHit.ReactionIntent = sourceHit.ReactionIntent;
                    targetHit.Damage = sourceHit.Damage;
                    targetHit.PoiseDamage = sourceHit.PoiseDamage;
                    targetHit.GuardDamage = sourceHit.GuardDamage;
                    targetHit.CanBeGuarded = sourceHit.CanBeGuarded;
                    targetHit.CanBePerfectGuarded = sourceHit.CanBePerfectGuarded;
                    targetHit.CanBePerfectEvaded = sourceHit.CanBePerfectEvaded;
                    targetHit.MaxHitsPerTarget = sourceHit.MaxHitsPerTarget;
                    targetHit.EffectiveRange = sourceHit.EffectiveRange;
                    targetHit.EffectiveAngle = sourceHit.EffectiveAngle;
                    targetHit.TriggersPerfectGuardBossStagger = sourceHit.TriggersPerfectGuardBossStagger;
                    targetHit.HitIndex = sourceHit.HitIndex;
                    targetHit.SourcePart = sourceHit.SourcePart;
                    break;
                case CombatReactionWindowClip targetReaction when source is CombatReactionWindowClip sourceReaction:
                    targetReaction.ReactionType = sourceReaction.ReactionType;
                    targetReaction.Direction = sourceReaction.Direction;
                    targetReaction.StunDuration = sourceReaction.StunDuration;
                    targetReaction.RecoveryStartTime = sourceReaction.RecoveryStartTime;
                    targetReaction.CanReturnTime = sourceReaction.CanReturnTime;
                    targetReaction.ReactionMotionId = sourceReaction.ReactionMotionId;
                    break;
                case BossReactionGateWindowClip targetBossGate when source is BossReactionGateWindowClip sourceBossGate:
                    targetBossGate.AttackTypes = sourceBossGate.AttackTypes != null
                        ? (CombatAttackType[])sourceBossGate.AttackTypes.Clone()
                        : Array.Empty<CombatAttackType>();
                    targetBossGate.Policy = sourceBossGate.Policy;
                    targetBossGate.BlockedOutcome = sourceBossGate.BlockedOutcome;
                    break;
                case CombatMotionReferenceClip targetMotion when source is CombatMotionReferenceClip sourceMotion:
                    targetMotion.MotionKind = sourceMotion.MotionKind;
                    targetMotion.PlayerMotionId = sourceMotion.PlayerMotionId;
                    targetMotion.BossAttackId = sourceMotion.BossAttackId;
                    targetMotion.ProfileWindowName = sourceMotion.ProfileWindowName;
                    break;
                case CombatAnimationClipWindow targetAnimation when source is CombatAnimationClipWindow sourceAnimation:
                    targetAnimation.PreviewAnimationClip = sourceAnimation.PreviewAnimationClip;
                    targetAnimation.AnimatorStateName = sourceAnimation.AnimatorStateName;
                    targetAnimation.ClipStartOffset = sourceAnimation.ClipStartOffset;
                    targetAnimation.ClipEndOffset = sourceAnimation.ClipEndOffset;
                    targetAnimation.ClipSpeed = sourceAnimation.ClipSpeed;
                    targetAnimation.LoopPreview = sourceAnimation.LoopPreview;
                    break;
                case CombatMarkerClip targetMarker when source is CombatMarkerClip sourceMarker:
                    targetMarker.MarkerId = sourceMarker.MarkerId;
                    break;
            }
        }

        private static TClip CopyBase<TClip>(CombatTimelineClip source, TClip target) where TClip : CombatTimelineClip
        {
            CopyBaseValues(source, target);
            return target;
        }

        private static void CopyBaseValues(CombatTimelineClip source, CombatTimelineClip target)
        {
            target.Name = source.Name;
            target.CapabilityId = source.CapabilityId;
            target.StartTime = source.StartTime;
            target.EndTime = source.EndTime;
        }

        /// <summary>
        /// 执行 Copy / Phase 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatPhaseClip CopyPhase(CombatPhaseClip source)
        {
            CombatPhaseClip target = CopyBase(source, new CombatPhaseClip());
            target.Phase = source.Phase;
            return target;
        }
        /// <summary>
        /// 执行 Copy / Hit 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatHitNodeClip CopyHit(CombatHitNodeClip source)
        {
            CombatHitNodeClip target = CopyBase(source, new CombatHitNodeClip());
            target.AttackType = source.AttackType;
            target.ReactionIntent = source.ReactionIntent;
            target.Damage = source.Damage;
            target.PoiseDamage = source.PoiseDamage;
            target.GuardDamage = source.GuardDamage;
            target.CanBeGuarded = source.CanBeGuarded;
            target.CanBePerfectGuarded = source.CanBePerfectGuarded;
            target.CanBePerfectEvaded = source.CanBePerfectEvaded;
            target.MaxHitsPerTarget = source.MaxHitsPerTarget;
            target.EffectiveRange = source.EffectiveRange;
            target.EffectiveAngle = source.EffectiveAngle;
            target.TriggersPerfectGuardBossStagger = source.TriggersPerfectGuardBossStagger;
            target.HitIndex = source.HitIndex;
            target.SourcePart = source.SourcePart;
            return target;
        }

        /// <summary>
        /// 执行 Copy / Reaction 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatReactionWindowClip CopyReaction(CombatReactionWindowClip source)
        {
            CombatReactionWindowClip target = CopyBase(source, new CombatReactionWindowClip());
            target.ReactionType = source.ReactionType;
            target.Direction = source.Direction;
            target.StunDuration = source.StunDuration;
            target.RecoveryStartTime = source.RecoveryStartTime;
            target.CanReturnTime = source.CanReturnTime;
            target.ReactionMotionId = source.ReactionMotionId;
            return target;
        }

        private static BossReactionGateWindowClip CopyBossReactionGate(BossReactionGateWindowClip source)
        {
            BossReactionGateWindowClip target = CopyBase(source, new BossReactionGateWindowClip());
            target.AttackTypes = source.AttackTypes != null
                ? (CombatAttackType[])source.AttackTypes.Clone()
                : Array.Empty<CombatAttackType>();
            target.Policy = source.Policy;
            target.BlockedOutcome = source.BlockedOutcome;
            return target;
        }

        /// <summary>
        /// 执行 Copy / Motion 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatMotionReferenceClip CopyMotion(CombatMotionReferenceClip source)
        {
            CombatMotionReferenceClip target = CopyBase(source, new CombatMotionReferenceClip());
            target.MotionKind = source.MotionKind;
            target.PlayerMotionId = source.PlayerMotionId;
            target.BossAttackId = source.BossAttackId;
            target.ProfileWindowName = source.ProfileWindowName;
            return target;
        }

        /// <summary>
        /// 执行 Copy / Animation 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatAnimationClipWindow CopyAnimation(CombatAnimationClipWindow source)
        {
            CombatAnimationClipWindow target = CopyBase(source, new CombatAnimationClipWindow());
            target.PreviewAnimationClip = source.PreviewAnimationClip;
            target.AnimatorStateName = source.AnimatorStateName;
            target.ClipStartOffset = source.ClipStartOffset;
            target.ClipEndOffset = source.ClipEndOffset;
            target.ClipSpeed = source.ClipSpeed;
            target.LoopPreview = source.LoopPreview;
            return target;
        }

        /// <summary>
        /// 执行 Copy / Marker 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static CombatMarkerClip CopyMarker(CombatMarkerClip source)
        {
            CombatMarkerClip target = CopyBase(source, new CombatMarkerClip());
            target.MarkerId = source.MarkerId;
            return target;
        }

        /// <summary>
        /// 执行 Clip / Color 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static Color ClipColor(CombatTimelineClip clip)
        {
            Color baseColor = clip.ClipKind switch
            {
                CombatTimelineClipKind.Phase => PhaseClipColor(clip),
                CombatTimelineClipKind.HitNode => new Color(0.84f, 0.22f, 0.18f),
                CombatTimelineClipKind.AttackWindow => new Color(0.9f, 0.55f, 0.16f),
                CombatTimelineClipKind.CancelWindow => new Color(0.28f, 0.55f, 0.95f),
                CombatTimelineClipKind.DefenseWindow => new Color(0.2f, 0.72f, 0.55f),
                CombatTimelineClipKind.ArmorWindow => new Color(0.58f, 0.48f, 0.9f),
                CombatTimelineClipKind.BossReactionGateWindow => new Color(0.95f, 0.62f, 0.18f),
                CombatTimelineClipKind.BossMotionWarp => new Color(0.65f, 0.52f, 0.22f),
                CombatTimelineClipKind.BossCodeMove => new Color(0.58f, 0.58f, 0.32f),
                CombatTimelineClipKind.RootMotionSuppress => new Color(0.48f, 0.48f, 0.48f),
                CombatTimelineClipKind.Reaction => new Color(0.75f, 0.34f, 0.62f),
                CombatTimelineClipKind.AnimationPreview => new Color(0.82f, 0.82f, 0.82f),
                _ => new Color(0.45f, 0.45f, 0.45f)
            };

            if (clip.ClipKind == CombatTimelineClipKind.Phase)
            {
                return baseColor;
            }

            return VaryClipColor(baseColor, clip);
        }

        /// <summary>
        /// 执行 Phase / Clip / Color 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static Color PhaseClipColor(CombatTimelineClip clip)
        {
            string name = clip.Name ?? string.Empty;
            if (name.IndexOf("Active", StringComparison.OrdinalIgnoreCase) >= 0 || clip.CapabilityId == CombatTimelineCapabilityId.Hit_Attack)
            {
                return new Color(0.86f, 0.52f, 0.18f);
            }

            if (name.IndexOf("Recovery", StringComparison.OrdinalIgnoreCase) >= 0 || clip.CapabilityId == CombatTimelineCapabilityId.Reaction_Recovery)
            {
                return new Color(0.36f, 0.46f, 0.58f);
            }

            if (name.IndexOf("Reset", StringComparison.OrdinalIgnoreCase) >= 0 || clip.CapabilityId == CombatTimelineCapabilityId.Cancel_AttackReset)
            {
                return new Color(0.56f, 0.56f, 0.56f);
            }

            if (name.IndexOf("Startup", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return new Color(0.34f, 0.34f, 0.34f);
            }

            return VaryClipColor(new Color(0.45f, 0.45f, 0.45f), clip);
        }

        /// <summary>
        /// 执行 Vary / Clip / Color 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static Color VaryClipColor(Color color, CombatTimelineClip clip)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            int hash = StableHash($"{clip.CapabilityId}:{clip.Name}");
            float hueOffset = ((hash % 7) - 3) * 0.012f;
            float valueOffset = (((hash / 7) % 5) - 2) * 0.045f;
            h = Mathf.Repeat(h + hueOffset, 1f);
            v = Mathf.Clamp01(v + valueOffset);
            return Color.HSVToRGB(h, s, v);
        }

        /// <summary>
        /// 执行 Stable / Hash 相关逻辑，并维护 Combat Timeline 编辑器 模块的运行时一致性。
        /// </summary>
        private static int StableHash(string value)
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = hash * 31 + value[i];
                }

                return Mathf.Abs(hash);
            }
        }


    }
}
