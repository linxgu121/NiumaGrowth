using System;
using System.Collections.Generic;
using NiumaGrowth.Data;
using NiumaUI.Toolkit;
using NiumaUI.Toolkit.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace NiumaGrowth.Bridge
{
    public sealed class GrowthToolkitReceiver : MonoBehaviour, IGrowthUIReceiver
    {
        [SerializeField, Tooltip("拖核心场景 UIRoot/UIManager 上的 UIToolkitUIManager。")]
        private UIToolkitUIManager uiManager;
        [SerializeField, Tooltip("成长面板 ViewId。默认 GrowthPanel，需要在 UIToolkitViewRegistrySO 注册。")]
        private string growthViewId = "GrowthPanel";
        [SerializeField, Tooltip("刷新失败时是否自动打开成长面板。")]
        private bool autoOpenView = true;
        [SerializeField, Tooltip("收到 Cleared 更新时是否关闭成长面板。关闭后会立即返回，不再重新打开。")]
        private bool closeOnCleared = true;
        [SerializeField, Tooltip("缺少 UIManager 或 View 时是否输出警告。")]
        private bool logWarnings = true;

        public void ApplyGrowthUpdate(GrowthUIUpdate update)
        {
            if (update.UpdateType == GrowthUIUpdateType.Cleared && closeOnCleared && uiManager != null)
            {
                uiManager.CloseView(growthViewId);
                return;
            }

            if (!EnsureUIManager())
                return;

            var refreshed = uiManager.RefreshView(growthViewId, update);
            if (!refreshed && autoOpenView)
                refreshed = uiManager.OpenView(growthViewId, update);

            if (!refreshed)
                Warn($"没有刷新到成长 Toolkit View：ViewId={growthViewId}。请检查 Registry 和 BindingProvider。");
        }

        private bool EnsureUIManager()
        {
            if (uiManager == null)
                uiManager = FindSceneObject<UIToolkitUIManager>();

            if (uiManager != null)
                return true;

            Warn("未绑定 UIToolkitUIManager，成长 Toolkit 面板无法刷新。");
            return false;
        }

        private void Warn(string message)
        {
            if (logWarnings && !string.IsNullOrWhiteSpace(message))
                UnityEngine.Debug.LogWarning($"[GrowthToolkitReceiver] {message}", this);
        }

        private static T FindSceneObject<T>() where T : UnityEngine.Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }

    public sealed class GrowthToolkitBindingProvider : ToolkitViewBindingProviderBase
    {
        [Header("元素名称")]
        [SerializeField, Tooltip("标题 Label 的 name。默认 TitleText。")]
        private string titleLabelName = "TitleText";
        [SerializeField, Tooltip("状态 Label 的 name。默认 StatusText。")]
        private string statusLabelName = "StatusText";
        [SerializeField, Tooltip("成长列表 ListView 的 name。默认 ListRoot。")]
        private string listViewName = "ListRoot";
        [SerializeField, Tooltip("详情 Label 的 name。用于显示当前选中技艺。")]
        private string detailLabelName = "DetailText";
        [SerializeField, Tooltip("结果 Label 的 name。成长面板通常留空。")]
        private string resultLabelName = "ResultText";
        [SerializeField, Tooltip("空状态节点的 name。没有成长数据时显示。")]
        private string emptyRootName = "EmptyRoot";

        [Header("列表")]
        [SerializeField, Tooltip("最多显示多少行。")]
        private int maxRows = 80;
        [SerializeField, Tooltip("列表行 USS class。")]
        private string rowClass = "niuma-growth-row";
        [SerializeField, Tooltip("选中行 USS class。")]
        private string selectedRowClass = "is-selected";
        [SerializeField, Tooltip("禁用行 USS class。")]
        private string disabledRowClass = "is-disabled";

        protected override string DefaultProviderId => "GrowthPanel";

        public override IToolkitViewBinding CreateBinding()
        {
            return new GrowthToolkitBinding(
                titleLabelName,
                statusLabelName,
                listViewName,
                detailLabelName,
                resultLabelName,
                emptyRootName,
                maxRows,
                rowClass,
                selectedRowClass,
                disabledRowClass);
        }
    }

    public sealed class GrowthToolkitViewModel : UIPanelViewModelBase
    {
        public readonly List<ToolkitTextRowData> Rows = new List<ToolkitTextRowData>();
        public GrowthPanelViewData Panel { get; private set; }
        public GrowthUIUpdateType UpdateType { get; private set; }
        public long Revision { get; private set; }
        public string SelectedSkillId { get; private set; }
        public GrowthProgressViewData SelectedSkill { get; private set; }

        public void Apply(GrowthUIUpdate update, int maxRows)
        {
            Panel = update.Current;
            UpdateType = update.UpdateType;
            Revision = update.Revision;
            SetContext(Panel?.ActorId);
            SelectedSkillId = NormalizeSelection(Panel, SelectedSkillId);
            RebuildRows(maxRows);
            MarkDirty();
        }

        public void Select(string skillId)
        {
            SelectedSkillId = string.IsNullOrWhiteSpace(skillId) ? null : skillId.Trim();
            RebuildRows(int.MaxValue);
            MarkDirty();
        }

        protected override void OnClear(UIViewModelClearReason reason)
        {
            Panel = null;
            UpdateType = GrowthUIUpdateType.Cleared;
            Revision = 0;
            SelectedSkillId = null;
            SelectedSkill = null;
            Rows.Clear();
        }

        private void RebuildRows(int maxRows)
        {
            Rows.Clear();
            SelectedSkill = null;
            var skills = Panel?.Skills ?? Array.Empty<GrowthProgressViewData>();
            var rowsLeft = Math.Max(1, maxRows);
            for (var i = 0; i < skills.Length && rowsLeft > 0; i++)
            {
                var skill = skills[i];
                if (skill == null)
                    continue;

                var id = string.IsNullOrWhiteSpace(skill.SkillId) ? $"growth:{i}" : skill.SkillId.Trim();
                var isSelected = string.Equals(SelectedSkillId, id, StringComparison.Ordinal);
                if (isSelected)
                    SelectedSkill = skill;

                var exp = skill.IsMaxLevel ? $"总经验 {skill.TotalExp} | 满级" : $"{skill.ExpInCurrentLevel}/{skill.ExpToNextLevel} ({skill.Progress01:P0}) | 总经验 {skill.TotalExp}";
                var flags = $"{(skill.IsInheritable ? " | 可继承" : string.Empty)}{(skill.IsMissingDefinition ? " | 缺失定义" : string.Empty)}";
                Rows.Add(new ToolkitTextRowData(id, $"{Text(skill.DisplayName, skill.SkillId)} Lv.{skill.Level} [{skill.Category}] | {exp}{flags}", isSelected, !skill.IsMissingDefinition, skill));
                rowsLeft--;
            }
        }

        private static string NormalizeSelection(GrowthPanelViewData panel, string previous)
        {
            var skills = panel?.Skills ?? Array.Empty<GrowthProgressViewData>();
            if (!string.IsNullOrWhiteSpace(previous))
            {
                for (var i = 0; i < skills.Length; i++)
                {
                    if (string.Equals(skills[i]?.SkillId, previous, StringComparison.Ordinal))
                        return previous.Trim();
                }
            }

            return skills.Length > 0 ? skills[0]?.SkillId : null;
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }

    public sealed class GrowthToolkitBinding : ToolkitViewBindingBase<GrowthUIUpdate, GrowthToolkitViewModel>
    {
        private readonly string _titleName;
        private readonly string _statusName;
        private readonly string _listName;
        private readonly string _detailName;
        private readonly string _resultName;
        private readonly string _emptyName;
        private readonly int _maxRows;
        private readonly string _rowClass;
        private readonly string _selectedClass;
        private readonly string _disabledClass;
        private readonly ToolkitListBinding<ToolkitTextRowData> _listBinding = new ToolkitListBinding<ToolkitTextRowData>();
        private Label _title;
        private Label _status;
        private Label _detail;
        private Label _result;

        public GrowthToolkitBinding(
            string titleName,
            string statusName,
            string listName,
            string detailName,
            string resultName,
            string emptyName,
            int maxRows,
            string rowClass,
            string selectedClass,
            string disabledClass)
        {
            _titleName = titleName;
            _statusName = statusName;
            _listName = listName;
            _detailName = detailName;
            _resultName = resultName;
            _emptyName = emptyName;
            _maxRows = Mathf.Max(1, maxRows);
            _rowClass = string.IsNullOrWhiteSpace(rowClass) ? "niuma-growth-row" : rowClass.Trim();
            _selectedClass = selectedClass;
            _disabledClass = disabledClass;
        }

        protected override void OnInitializeTyped()
        {
            _title = QLabel(_titleName);
            _status = QLabel(_statusName);
            _detail = QLabel(_detailName);
            _result = QLabel(_resultName);
            _listBinding.Bind(Root, _listName, new ToolkitTextRowItemBinder(_rowClass, _selectedClass, _disabledClass, HandleRowClicked), _emptyName);
            ApplyVisualState(ViewModel);
        }

        protected override void OnRefreshTyped(GrowthUIUpdate viewData, GrowthToolkitViewModel viewModel)
        {
            viewModel.Apply(viewData, _maxRows);
            ApplyVisualState(viewModel);
        }

        protected override void OnClearTyped(UIViewModelClearReason reason)
        {
            _listBinding.Clear();
            ApplyVisualState(ViewModel);
        }

        protected override void OnDisposeTyped()
        {
            _listBinding.Dispose();
        }

        private void HandleRowClicked(ToolkitTextRowData row)
        {
            if (row == null)
                return;

            ViewModel.Select(row.Id);
            ApplyVisualState(ViewModel);
        }

        private void ApplyVisualState(GrowthToolkitViewModel viewModel)
        {
            var panel = viewModel?.Panel;
            SetText(_title, "成长");
            SetText(_status, panel == null
                ? $"状态：{viewModel?.UpdateType ?? GrowthUIUpdateType.Cleared}"
                : $"Actor {Text(panel.ActorId, "未知")} | Revision {panel.Revision} | 技艺 {panel.Skills?.Length ?? 0}");
            SetText(_detail, viewModel?.SelectedSkill != null ? Detail(viewModel.SelectedSkill) : panel == null ? "暂无成长数据。" : "未选择技艺。");
            SetText(_result, string.Empty);
            _listBinding.ReplaceAll(viewModel?.Rows ?? new List<ToolkitTextRowData>());
        }

        private static string Detail(GrowthProgressViewData skill)
        {
            if (skill == null)
                return "未选择技艺。";

            var exp = skill.IsMaxLevel ? "满级" : $"本级 {skill.ExpInCurrentLevel}/{skill.ExpToNextLevel} ({skill.Progress01:P0})";
            return $"选中：{Text(skill.DisplayName, skill.SkillId)}\n等级：Lv.{skill.Level}\n分类：{skill.Category}\n经验：{exp}\n总经验：{skill.TotalExp}\n说明：{skill.Description}".Trim();
        }

        private static string Text(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
        }
    }
}