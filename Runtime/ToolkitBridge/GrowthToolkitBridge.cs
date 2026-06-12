using NiumaGrowth.Data;
using NiumaUI.Toolkit;
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
        [SerializeField] private bool autoOpenView = true;
        [SerializeField] private bool closeOnCleared = true;
        [SerializeField] private bool logWarnings = true;

        public void ApplyGrowthUpdate(GrowthUIUpdate update)
        {
            if (update.UpdateType == GrowthUIUpdateType.Cleared && closeOnCleared && uiManager != null) uiManager.CloseView(growthViewId);
            if (!EnsureUIManager()) return;
            var refreshed = uiManager.RefreshView(growthViewId, update);
            if (!refreshed && autoOpenView) refreshed = uiManager.OpenView(growthViewId, update);
            if (!refreshed) Warn($"没有刷新到成长 Toolkit View：ViewId={growthViewId}。请检查 Registry 和 BindingProvider。");
        }

        private bool EnsureUIManager()
        {
            if (uiManager == null) uiManager = FindSceneObject<UIToolkitUIManager>();
            if (uiManager != null) return true;
            Warn("未绑定 UIToolkitUIManager，成长 Toolkit 面板无法刷新。");
            return false;
        }

        private void Warn(string message)
        {
            if (logWarnings && !string.IsNullOrWhiteSpace(message)) UnityEngine.Debug.LogWarning($"[GrowthToolkitReceiver] {message}", this);
        }

        private static T FindSceneObject<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>();
#else
            return FindObjectOfType<T>();
#endif
        }
    }

    public sealed class GrowthToolkitBindingProvider : MonoBehaviour, IToolkitViewBindingProvider
    {
        [SerializeField, Tooltip("BindingProviderId，默认 GrowthPanel。需要和 Registry 一致。")]
        private string providerId = "GrowthPanel";
        [SerializeField] private string titleLabelName = "TitleText";
        [SerializeField] private string statusLabelName = "StatusText";
        [SerializeField] private string listRootName = "ListRoot";
        [SerializeField] private string detailLabelName = "DetailText";
        [SerializeField] private string resultLabelName = "ResultText";
        [SerializeField] private string emptyRootName = "EmptyRoot";
        [SerializeField] private int maxRows = 80;
        [SerializeField] private string rowClass = "niuma-growth-row";

        public string ProviderId => string.IsNullOrWhiteSpace(providerId) ? "GrowthPanel" : providerId.Trim();
        public IToolkitViewBinding CreateBinding() => new GrowthToolkitBinding(titleLabelName, statusLabelName, listRootName, detailLabelName, resultLabelName, emptyRootName, maxRows, rowClass);
    }

    public sealed class GrowthToolkitBinding : ToolkitViewBindingBase
    {
        private readonly string _titleName, _statusName, _listName, _detailName, _resultName, _emptyName, _rowClass;
        private readonly int _maxRows;
        private Label _title, _status, _detail, _result;
        private VisualElement _list, _empty;

        public GrowthToolkitBinding(string titleName, string statusName, string listName, string detailName, string resultName, string emptyName, int maxRows, string rowClass)
        {
            _titleName = titleName; _statusName = statusName; _listName = listName; _detailName = detailName; _resultName = resultName; _emptyName = emptyName;
            _maxRows = Mathf.Max(1, maxRows);
            _rowClass = string.IsNullOrWhiteSpace(rowClass) ? "niuma-growth-row" : rowClass.Trim();
        }

        protected override void OnInitialize()
        {
            _title = QL(_titleName); _status = QL(_statusName); _list = QE(_listName); _detail = QL(_detailName); _result = QL(_resultName); _empty = QE(_emptyName);
            Apply(null, GrowthUIUpdateType.Cleared, 0);
        }

        protected override void OnRefresh(object viewData)
        {
            if (viewData is GrowthUIUpdate update) Apply(update.Current, update.UpdateType, update.Revision);
            else Apply(null, GrowthUIUpdateType.Cleared, 0);
        }

        protected override void OnClose() => Apply(null, GrowthUIUpdateType.Cleared, 0);

        private void Apply(GrowthPanelViewData panel, GrowthUIUpdateType updateType, long revision)
        {
            Clear();
            var skills = panel?.Skills ?? System.Array.Empty<GrowthProgressViewData>();
            Set(_title, "成长");
            SetVisible(_empty, panel == null || skills.Length == 0);
            Set(_status, panel == null ? $"状态：{updateType}" : $"Actor {Text(panel.ActorId, "未知")} | Revision {panel.Revision} | 技艺 {skills.Length}");
            Set(_detail, panel == null ? "暂无成长数据。" : "等级由 TotalExp 和当前配置阈值实时计算，UI 不保存等级事实。");
            Set(_result, string.Empty);

            for (var i = 0; i < skills.Length && i < _maxRows; i++)
            {
                var s = skills[i];
                if (s == null) continue;
                var exp = s.IsMaxLevel ? $"总经验 {s.TotalExp} | 满级" : $"{s.ExpInCurrentLevel}/{s.ExpToNextLevel} ({s.Progress01:P0}) | 总经验 {s.TotalExp}";
                Add($"{Text(s.DisplayName, s.SkillId)} Lv.{s.Level} [{s.Category}] | {exp}{(s.IsInheritable ? " | 可继承" : string.Empty)}{(s.IsMissingDefinition ? " | 缺失定义" : string.Empty)}");
            }
        }

        private Label QL(string name) => string.IsNullOrWhiteSpace(name) ? null : Query<Label>(name.Trim());
        private VisualElement QE(string name) => string.IsNullOrWhiteSpace(name) ? null : Root?.Q<VisualElement>(name.Trim());
        private void Clear() { if (_list != null) _list.Clear(); }
        private void Add(string text) { if (_list == null) return; var row = new Label(text ?? string.Empty); row.AddToClassList(_rowClass); _list.Add(row); }
        private static void Set(Label label, string text) { if (label != null) label.text = text ?? string.Empty; }
        private static void SetVisible(VisualElement element, bool visible) { if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None; }
        private static string Text(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback ?? string.Empty : value;
    }
}
