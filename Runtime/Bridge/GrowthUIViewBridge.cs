using System;
using NiumaGrowth.Controller;
using NiumaGrowth.Data;
using UnityEngine;

namespace NiumaGrowth.Bridge
{
    /// <summary>
    /// 成长模块到 UI 的数据驱动桥接层。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GrowthUIViewBridge : MonoBehaviour
    {
        [Header("模块引用")]
        [Tooltip("成长模块根控制器。请拖入场景中的 NiumaGrowthController；为空时可自动查找。")]
        [SerializeField] private NiumaGrowthController growthController;

        [Tooltip("实现 IGrowthUIReceiver 的 UI 组件。")]
        [SerializeField] private MonoBehaviour growthUIReceiverProvider;

        [Header("刷新策略")]
        [Tooltip("没有手动绑定控制器时是否自动查找。正式场景建议手动绑定。")]
        [SerializeField] private bool autoFindGrowthController = true;

        [Tooltip("启用时是否立即刷新一次。")]
        [SerializeField] private bool refreshOnEnable = true;

        [Tooltip("是否在 LateUpdate 中按 Revision 自动刷新。")]
        [SerializeField] private bool refreshInLateUpdate = true;

        [Tooltip("没有可显示数据时是否通知 UI 清空。")]
        [SerializeField] private bool notifyWhenCleared = true;

        [Header("显示对象")]
        [Tooltip("当前显示的 ActorId。玩家可填 player。")]
        [SerializeField] private string actorId = "player";

        [Tooltip("是否显示尚未获得经验的技艺配置。")]
        [SerializeField] private bool includeNotStarted = true;

        [Header("日志")]
        [SerializeField] private bool logWarnings = true;

        private IGrowthUIReceiver _receiver;
        private long _observedRevision = -1L;
        private GrowthPanelViewData _lastPanelData;
        private bool _hadPanelData;
        private bool _isApplyingUpdate;
        private bool _refreshRequested;

        private void OnEnable()
        {
            ResolveReferences(true);
            _observedRevision = -1L;
            _isApplyingUpdate = false;
            if (refreshOnEnable) RefreshGrowthPanel();
        }

        private void OnDisable()
        {
            _isApplyingUpdate = false;
            _refreshRequested = false;
        }

        private void LateUpdate()
        {
            if (_refreshRequested)
            {
                _refreshRequested = false;
                RefreshGrowthPanel();
                return;
            }

            if (!refreshInLateUpdate || !EnsureController()) return;
            if (_observedRevision != growthController.GrowthRevision)
            {
                RefreshGrowthPanel();
            }
        }

        public void RefreshGrowthPanel()
        {
            if (!EnsureController() || string.IsNullOrWhiteSpace(actorId))
            {
                ApplyClearUpdate();
                return;
            }

            var revision = growthController.GrowthRevision;
            var data = new GrowthPanelViewData
            {
                ActorId = actorId,
                Revision = revision,
                Skills = growthController.GetAllProgress(actorId, includeNotStarted)
            };

            _observedRevision = revision;
            if (data.Skills == null || data.Skills.Length == 0)
            {
                ApplyClearUpdate();
                return;
            }

            _hadPanelData = true;
            ApplyRawUpdate(new GrowthUIUpdate(GrowthUIUpdateType.Refresh, revision, data, _lastPanelData));
            _lastPanelData = data;
        }

        public void SetActorId(string value)
        {
            if (!string.Equals(actorId, value, StringComparison.Ordinal))
            {
                _lastPanelData = null;
                _hadPanelData = false;
            }

            actorId = value;
            RequestRefresh();
        }

        private void ApplyClearUpdate()
        {
            _observedRevision = growthController != null ? growthController.GrowthRevision : -1L;
            if (!notifyWhenCleared && !_hadPanelData) return;
            ApplyRawUpdate(new GrowthUIUpdate(GrowthUIUpdateType.Cleared, _observedRevision, null, _lastPanelData));
            _lastPanelData = null;
            _hadPanelData = false;
        }

        private void ApplyRawUpdate(GrowthUIUpdate update)
        {
            _receiver = ResolveReceiver(true);
            if (_receiver == null) return;
            if (_isApplyingUpdate)
            {
                _refreshRequested = true;
                if (logWarnings) Debug.LogWarning("[NiumaGrowthUIBridge] 检测到 UI 刷新回流，已延后到下一帧。", this);
                return;
            }

            var revisionBefore = growthController != null ? growthController.GrowthRevision : update.Revision;
            try
            {
                _isApplyingUpdate = true;
                _receiver.ApplyGrowthUpdate(update);
            }
            finally
            {
                _isApplyingUpdate = false;
            }

            if (growthController != null && growthController.GrowthRevision != revisionBefore)
            {
                _observedRevision = -1L;
                _refreshRequested = true;
            }
        }

        private void RequestRefresh()
        {
            _observedRevision = -1L;
            _refreshRequested = true;
        }

        private bool EnsureController()
        {
            ResolveGrowthController(true);
            return growthController != null;
        }

        private void ResolveReferences(bool logMissing)
        {
            ResolveGrowthController(logMissing);
            _receiver = ResolveReceiver(logMissing);
        }

        private void ResolveGrowthController(bool logMissing)
        {
            if (growthController != null) return;
            if (autoFindGrowthController)
            {
#if UNITY_2023_1_OR_NEWER
                growthController = FindFirstObjectByType<NiumaGrowthController>();
#else
                growthController = FindObjectOfType<NiumaGrowthController>();
#endif
            }

            if (growthController == null && logWarnings && logMissing)
            {
                Debug.LogWarning("[NiumaGrowthUIBridge] 未找到 NiumaGrowthController，请在 Inspector 中绑定。", this);
            }
        }

        private IGrowthUIReceiver ResolveReceiver(bool logMissing)
        {
            var receiver = growthUIReceiverProvider as IGrowthUIReceiver;
            if (receiver == null && growthUIReceiverProvider != null && logWarnings && logMissing)
            {
                Debug.LogWarning("[NiumaGrowthUIBridge] Receiver Provider 没有实现 IGrowthUIReceiver。", this);
            }

            return receiver;
        }
    }
}
