using CommunityToolkit.Mvvm.Input;
using IcoConverter.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;

namespace IcoConverter.ViewModels
{
    public partial class MainViewModel
    {
        // 当前已应用的裁剪结果。未裁剪时与原图共用位图。
        private BitmapSource? _workingImage;
        // 主体对齐结果单独保留，清除对齐时可直接回到当前裁剪结果。
        private BitmapSource? _sourceOverride;

        // 裁剪预览使用未裁剪的图像，可降采样。裁剪选区按预览比例换算。
        private BitmapSource? _cropPreviewSource;
        private double _cropPreviewScale = 1d;
        private Rect _cropSelection = Rect.Empty;
        private CropRegion? _appliedCropRegion;
        private bool _isCropModeEnabled;
        private double _imageScale = 1d;
        private double _imageOffsetX;
        private double _imageOffsetY;
        private double _imageOffsetLimit = DefaultImageOffsetLimit;

        private int _trimPaddingPercent = 8;
        private const double MinImageScale = 0.1d;
        private const double MaxImageScale = 4d;
        private const double DefaultImageOffsetLimit = 2048d;

        // 拖动滑块会频繁触发 setter，该时间窗内的变更合并为一次撤销记录
        private const int UndoCoalesceMilliseconds = 400;
        private const int MaxUndoDepth = 100;

        private readonly Stack<MaskStateSnapshot> _undoStack = new();
        private readonly Stack<MaskStateSnapshot> _redoStack = new();
        private bool _suspendUndoCapture;
        private bool _isRestoringState;
        private long _lastUndoCaptureTimestamp;

        public IRelayCommand ToggleCropModeCommand { get; }
        public IAsyncRelayCommand ApplyCropCommand { get; }
        // 退出裁剪模式，已应用的裁剪结果保留
        public IRelayCommand CancelCropCommand { get; }
        // 撤销裁剪本身，回到整张原图
        public IAsyncRelayCommand ResetCropCommand { get; }
        public IRelayCommand UndoCommand { get; }
        public IRelayCommand RedoCommand { get; }

        public IAsyncRelayCommand AutoFitContentCommand { get; }
        public IRelayCommand ClearSourceOverrideCommand { get; }
        public ObservableCollection<string> MaskHistory { get; } = new();

        private BitmapSource? WorkingSource => _sourceOverride ?? _workingImage;

        public bool HasSourceOverride => _sourceOverride != null;

        public string SourceOverrideHint => _sourceOverride == null
            ? string.Empty
            : $"已对齐：{_sourceOverride.PixelWidth} × {_sourceOverride.PixelHeight} px";

        public int TrimPaddingPercent
        {
            get => _trimPaddingPercent;
            set => SetProperty(ref _trimPaddingPercent, Math.Clamp(value, 0, 45));
        }

        public bool IsCropModeEnabled
        {
            get => _isCropModeEnabled;
            set
            {
                if (_isCropModeEnabled == value)
                {
                    return;
                }

                _isCropModeEnabled = value;

                _pendingPreviewUpdate = false;
                _previewDebounceTimer.Stop();

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCropOverlayVisible));
                OnPropertyChanged(nameof(IsMaskGuideVisible));
                OnPropertyChanged(nameof(PreviewContentMargin));
                OnPropertyChanged(nameof(PreviewModeHint));
                NotifyCommandStateChanges();

                if (value)
                {
                    EnsureCropPreviewSource();
                    PreviewImage = _cropPreviewSource ?? WorkingSource;
                    AddLog("已进入裁剪模式：拖动裁剪框调整范围，Enter 应用，Esc 取消。");
                }
                else
                {
                    SchedulePreviewUpdate();
                }
            }
        }

        public bool IsCropOverlayVisible => _isCropModeEnabled && _originalImage != null;

        // 裁剪模式下顶部需预留浮动操作条的空间
        public Thickness PreviewContentMargin => _isCropModeEnabled
            ? new Thickness(8, 68, 8, 8)
            : new Thickness(8);

        public bool HasCropApplied
        {
            get
            {
                if (_originalImage == null || _appliedCropRegion is not { } region)
                {
                    return false;
                }

                return !IsFullFrameSelection(region.ToRect());
            }
        }

        // _cropSelection 保存原图像素坐标。叠加层绑定的选区使用预览位图坐标。
        public Rect CropSelection
        {
            get => ToCropPreviewRect(_cropSelection);
            set
            {
                if (_originalImage == null)
                {
                    return;
                }

                var sourceRect = FromCropPreviewRect(value);
                var clamped = ClampCropSelection(sourceRect);
                if (AreClose(clamped, _cropSelection))
                {
                    return;
                }

                _cropSelection = clamped;
                OnPropertyChanged();
                NotifyCropMetricsChanged();
            }
        }

        public double ImageScale
        {
            get => _imageScale;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, MinImageScale, MaxImageScale);
                if (SetProperty(ref _imageScale, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetX
        {
            get => _imageOffsetX;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, ImageOffsetMinimum, ImageOffsetMaximum);
                if (SetProperty(ref _imageOffsetX, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetY
        {
            get => _imageOffsetY;
            set
            {
                var snapshot = CreateMaskStateSnapshot();
                var clamped = Math.Clamp(value, ImageOffsetMinimum, ImageOffsetMaximum);
                if (SetProperty(ref _imageOffsetY, clamped))
                {
                    RegisterUndoSnapshot(snapshot);
                    SchedulePreviewUpdate();
                }
            }
        }

        public double ImageOffsetLimit
        {
            get => _imageOffsetLimit;
            private set
            {
                if (SetProperty(ref _imageOffsetLimit, value))
                {
                    OnPropertyChanged(nameof(ImageOffsetMinimum));
                    OnPropertyChanged(nameof(ImageOffsetMaximum));
                }
            }
        }

        public double ImageOffsetMinimum => -ImageOffsetLimit;

        public double ImageOffsetMaximum => ImageOffsetLimit;

        public bool IsSquareCanvas
        {
            get
            {
                var source = WorkingSource ?? _originalImage;
                return source != null && Math.Abs(source.PixelWidth - source.PixelHeight) <= 1;
            }
        }

        private bool CanEditCrop() => !IsProcessing && _originalImage != null;

        // 裁剪模式的选区基于完整原图，退出该模式后才能对齐主体。
        private bool CanEditSource() => !IsProcessing && !_isCropModeEnabled && _originalImage != null;

        private bool CanClearSourceOverride() => !IsProcessing && _sourceOverride != null;

        private async Task AutoFitContentAsync()
        {
            var source = WorkingSource ?? _originalImage;
            if (source == null)
            {
                return;
            }

            var padding = TrimPaddingPercent;

            try
            {
                IsProcessing = true;
                AddLog($"自动对齐主体：留白 {padding}%，源图 {source.PixelWidth}×{source.PixelHeight}");

                var snapshot = CreateMaskStateSnapshot();
                var trimmed = await Task.Run(() => _imageProcessor.TrimToContent(source, padding));

                if (ReferenceEquals(trimmed, source))
                {
                    AddLog("主体已经顶满画布，没有可裁的空白。");
                    UpdateNotification("自动对齐", "这张图四周没有可裁的空白，保持原样。", NotificationLevel.Informational);
                    return;
                }

                ApplySourceOverride(trimmed);
                RegisterUndoSnapshot(snapshot);
                AddLog($"主体已对齐：{source.PixelWidth}×{source.PixelHeight} → {trimmed.PixelWidth}×{trimmed.PixelHeight}");
                UpdateNotification("自动对齐", SourceOverrideHint, NotificationLevel.Success);
            }
            catch (Exception ex)
            {
                ReportError("自动对齐主体失败", ex);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void ClearSourceOverride()
        {
            if (_sourceOverride == null)
            {
                return;
            }

            var snapshot = CreateMaskStateSnapshot();
            ApplySourceOverride(null);
            RegisterUndoSnapshot(snapshot);
            AddLog("已还原到对齐前的源图。");
        }

        private void ApplySourceOverride(BitmapSource? image)
        {
            _sourceOverride = image;

            var baseImage = image ?? _workingImage;
            if (baseImage != null)
            {
                RebuildPreviewPipeline(baseImage);
                UpdateHighResolutionOptions(baseImage);
            }

            InvalidateProcessedCaches();
            OnPropertyChanged(nameof(HasSourceOverride));
            OnPropertyChanged(nameof(SourceOverrideHint));
            OnPropertyChanged(nameof(CornerRadiusHint));
            NotifyCommandStateChanges();
            SchedulePreviewUpdate();
        }

        private bool CanApplyCrop() => !IsProcessing && _originalImage != null && _isCropModeEnabled;

        private bool CanCancelCrop() => !IsProcessing && _isCropModeEnabled;

        private bool CanResetCrop() => !IsProcessing && _originalImage != null && (HasCropApplied || _isCropModeEnabled);

        private void ToggleCropMode()
        {
            if (!_isCropModeEnabled && _cropSelection.IsEmpty && _originalImage != null)
            {
                _cropSelection = new Rect(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight);
                OnPropertyChanged(nameof(CropSelection));
            }

            IsCropModeEnabled = !_isCropModeEnabled;
        }

        private void CancelCrop()
        {
            if (!_isCropModeEnabled)
            {
                return;
            }

            // 用户取消时保留原有裁剪结果
            if (_originalImage != null)
            {
                _cropSelection = _appliedCropRegion?.ToRect()
                    ?? new Rect(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight);
                OnPropertyChanged(nameof(CropSelection));
                NotifyCropMetricsChanged();
            }

            IsCropModeEnabled = false;
            AddLog("已取消裁剪调整。");
        }

        private async Task ApplyCropAsync()
        {
            if (_originalImage == null || !_isCropModeEnabled)
            {
                return;
            }

            var region = CropRegion.FromRect(_cropSelection, _originalImage.PixelWidth, _originalImage.PixelHeight);
            if (region == null || _cropSelection.IsEmpty || _cropSelection.Width < 1 || _cropSelection.Height < 1)
            {
                ReportWarning("裁剪区域无效，请重新选择范围。");
                return;
            }

            if (IsFullFrameSelection(region.Value.ToRect()))
            {
                if (HasCropApplied)
                {
                    await ResetCropAsync();
                }
                else
                {
                    IsCropModeEnabled = false;
                    AddLog("裁剪区域仍是整张原图，未做裁剪。");
                }

                return;
            }

            var snapshot = CreateMaskStateSnapshot();
            IsProcessing = true;
            try
            {
                var source = _originalImage;
                var cropped = await Task.Run(() => _imageProcessor.Crop(source, region.Value));
                ApplyWorkingImage(cropped, region.Value);

                _isCropModeEnabled = false;
                OnPropertyChanged(nameof(IsCropModeEnabled));
                OnPropertyChanged(nameof(IsCropOverlayVisible));
                OnPropertyChanged(nameof(PreviewContentMargin));
                OnPropertyChanged(nameof(PreviewModeHint));

                RegisterUndoSnapshot(snapshot);
                AddLog($"已裁剪: {region.Value.Width}×{region.Value.Height}（区域 X={region.Value.X}, Y={region.Value.Y}）。");
                AddHistoryEntry($"裁剪 {region.Value.Width}×{region.Value.Height}");

                await ApplyCornerRadiusAsync();
            }
            catch (Exception ex)
            {
                ReportError("裁剪失败", ex);
            }
            finally
            {
                IsProcessing = false;
                NotifyCommandStateChanges();
            }
        }

        private async Task ResetCropAsync()
        {
            if (_originalImage == null)
            {
                return;
            }

            if (!HasCropApplied)
            {
                if (_isCropModeEnabled)
                {
                    _cropSelection = new Rect(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight);
                    OnPropertyChanged(nameof(CropSelection));
                    NotifyCropMetricsChanged();
                    AddLog("裁剪框已重置为整张原图。");
                }

                return;
            }

            var snapshot = CreateMaskStateSnapshot();
            IsProcessing = true;
            try
            {
                var full = new CropRegion(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight);
                ApplyWorkingImage(_originalImage, full);

                _isCropModeEnabled = false;
                OnPropertyChanged(nameof(IsCropModeEnabled));
                OnPropertyChanged(nameof(IsCropOverlayVisible));
                OnPropertyChanged(nameof(PreviewContentMargin));
                OnPropertyChanged(nameof(PreviewModeHint));

                RegisterUndoSnapshot(snapshot);
                AddLog("已取消裁剪，恢复整张原图。");
                AddHistoryEntry("取消裁剪");

                await ApplyCornerRadiusAsync();
            }
            finally
            {
                IsProcessing = false;
                NotifyCommandStateChanges();
            }
        }

        private void NotifyCropMetricsChanged()
        {
            OnPropertyChanged(nameof(HasCropApplied));
            OnPropertyChanged(nameof(IsOutputSizeVisible));
            OnPropertyChanged(nameof(OutputSizeText));
        }

        private bool IsFullFrameSelection(Rect selection)
        {
            if (_originalImage == null || selection.IsEmpty)
            {
                return true;
            }

            return selection.X <= 0.5
                && selection.Y <= 0.5
                && selection.Width >= _originalImage.PixelWidth - 0.5
                && selection.Height >= _originalImage.PixelHeight - 0.5;
        }

        private static bool AreClose(Rect a, Rect b) =>
            Math.Abs(a.X - b.X) < 0.01
            && Math.Abs(a.Y - b.Y) < 0.01
            && Math.Abs(a.Width - b.Width) < 0.01
            && Math.Abs(a.Height - b.Height) < 0.01;

        private Rect ClampCropSelection(Rect selection)
        {
            if (_originalImage == null)
            {
                return selection;
            }

            var boundsWidth = (double)_originalImage.PixelWidth;
            var boundsHeight = (double)_originalImage.PixelHeight;
            var width = Math.Clamp(selection.Width, 1, boundsWidth);
            var height = Math.Clamp(selection.Height, 1, boundsHeight);
            var x = Math.Clamp(selection.X, 0, Math.Max(0, boundsWidth - width));
            var y = Math.Clamp(selection.Y, 0, Math.Max(0, boundsHeight - height));
            return new Rect(x, y, width, height);
        }

        private Rect ToCropPreviewRect(Rect sourceRect)
        {
            if (sourceRect.IsEmpty || _cropPreviewScale <= 0)
            {
                return sourceRect;
            }

            return new Rect(
                sourceRect.X * _cropPreviewScale,
                sourceRect.Y * _cropPreviewScale,
                sourceRect.Width * _cropPreviewScale,
                sourceRect.Height * _cropPreviewScale);
        }

        private Rect FromCropPreviewRect(Rect previewRect)
        {
            if (previewRect.IsEmpty || _cropPreviewScale <= 0)
            {
                return previewRect;
            }

            return new Rect(
                previewRect.X / _cropPreviewScale,
                previewRect.Y / _cropPreviewScale,
                previewRect.Width / _cropPreviewScale,
                previewRect.Height / _cropPreviewScale);
        }

        private void EnsureCropPreviewSource()
        {
            if (_originalImage == null)
            {
                return;
            }

            if (_cropPreviewSource == null)
            {
                _cropPreviewSource = CreatePreviewPipelineSource(_originalImage, out _cropPreviewScale);
            }
        }

        private void ApplyWorkingImage(BitmapSource image, CropRegion region)
        {
            _workingImage = image;
            _appliedCropRegion = region;
            _cropSelection = region.ToRect();

            RebuildPreviewPipeline(image);
            UpdateCornerRadiusMaximum(image);
            UpdateHighResolutionOptions(image);
            ResetImageTransform();
            UpdateImageTransformLimits(image);
            InvalidateProcessedCaches();

            OnPropertyChanged(nameof(CropSelection));
            NotifyCropMetricsChanged();
        }

        // 撤销与重做经此路径执行，不再压入栈
        private void RestoreCropRegion(CropRegion? region)
        {
            if (_originalImage == null)
            {
                return;
            }

            var target = region ?? new CropRegion(0, 0, _originalImage.PixelWidth, _originalImage.PixelHeight);
            if (_appliedCropRegion == target)
            {
                return;
            }

            var image = _imageProcessor.Crop(_originalImage, target);
            _workingImage = image;
            _appliedCropRegion = target;
            _cropSelection = target.ToRect();

            // 裁剪变更后，基于旧图计算的对齐结果失效
            if (_sourceOverride != null)
            {
                _sourceOverride = null;
                OnPropertyChanged(nameof(HasSourceOverride));
                OnPropertyChanged(nameof(SourceOverrideHint));
            }

            RebuildPreviewPipeline(image);
            UpdateHighResolutionOptions(image);
            InvalidateProcessedCaches();

            OnPropertyChanged(nameof(CropSelection));
            NotifyCropMetricsChanged();
        }

        private void UpdateImageTransformLimits(BitmapSource? source)
        {
            double limit = DefaultImageOffsetLimit;

            if (source != null)
            {
                limit = Math.Max(source.PixelWidth, source.PixelHeight);
            }

            ImageOffsetLimit = limit;
            ClampImageOffsets();
        }

        private void ClampImageOffsets()
        {
            var clampedX = Math.Clamp(ImageOffsetX, ImageOffsetMinimum, ImageOffsetMaximum);
            var clampedY = Math.Clamp(ImageOffsetY, ImageOffsetMinimum, ImageOffsetMaximum);

            if (Math.Abs(clampedX - ImageOffsetX) > 0.0001)
            {
                _suspendUndoCapture = true;
                ImageOffsetX = clampedX;
                _suspendUndoCapture = false;
            }

            if (Math.Abs(clampedY - ImageOffsetY) > 0.0001)
            {
                _suspendUndoCapture = true;
                ImageOffsetY = clampedY;
                _suspendUndoCapture = false;
            }
        }

        private void ResetImageTransform()
        {
            _suspendUndoCapture = true;
            ImageScale = 1d;
            ImageOffsetX = 0d;
            ImageOffsetY = 0d;
            _suspendUndoCapture = false;
        }

        private MaskStateSnapshot CreateMaskStateSnapshot() => new(
            _cornerRadiusPixels,
            _cornerRadiusPercent,
            CornerRadiusUnit,
            SelectedMaskShape,
            CircleFitMode,
            CirclePaddingPercent,
            ExpandCanvasPixels,
            CanvasFillMode,
            NormalizeCanvasColorHex(CanvasCustomColorHex),
            FillCanvasOutsideMask,
            Math.Round(ImageScale, 4),
            Math.Round(ImageOffsetX, 2),
            Math.Round(ImageOffsetY, 2),
            _appliedCropRegion,
            _sourceOverride);

        private void RegisterUndoSnapshot(MaskStateSnapshot snapshot)
        {
            if (_suspendUndoCapture || _isRestoringState)
            {
                return;
            }

            if (_undoStack.TryPeek(out var top) && top.Equals(snapshot))
            {
                return;
            }

            var now = Stopwatch.GetTimestamp();
            var elapsedMs = _lastUndoCaptureTimestamp == 0
                ? double.MaxValue
                : (now - _lastUndoCaptureTimestamp) * 1000d / Stopwatch.Frequency;
            var coalesce = elapsedMs <= UndoCoalesceMilliseconds;

            if (!coalesce)
            {
                _undoStack.Push(snapshot);
                TrimUndoStack();
            }

            // 合并时间窗内不再入栈，一次撤销即回到拖动前状态
            _lastUndoCaptureTimestamp = now;
            _redoStack.Clear();
            NotifyCommandStateChanges();
        }

        private void TrimUndoStack()
        {
            if (_undoStack.Count <= MaxUndoDepth)
            {
                return;
            }

            // Stack 只能自栈顶枚举，故取前 N 项反转后重新压入，等价于丢弃最旧记录
            var kept = _undoStack.Take(MaxUndoDepth).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in kept)
            {
                _undoStack.Push(item);
            }
        }

        private bool CanUndo() => !IsProcessing && !_isCropModeEnabled && _undoStack.Count > 0;

        private bool CanRedo() => !IsProcessing && !_isCropModeEnabled && _redoStack.Count > 0;

        private void Undo()
        {
            if (!CanUndo())
            {
                return;
            }

            var current = CreateMaskStateSnapshot();
            var previous = _undoStack.Pop();
            _redoStack.Push(current);
            // 重置计时，下次改动单独入栈
            _lastUndoCaptureTimestamp = 0;
            RestoreMaskState(previous, "撤销");
        }

        private void Redo()
        {
            if (!CanRedo())
            {
                return;
            }

            var current = CreateMaskStateSnapshot();
            var next = _redoStack.Pop();
            _undoStack.Push(current);
            _lastUndoCaptureTimestamp = 0;
            RestoreMaskState(next, "重做");
        }

        private void RestoreMaskState(MaskStateSnapshot snapshot, string logPrefix)
        {
            _suspendUndoCapture = true;
            _isRestoringState = true;
            try
            {
                RestoreCropRegion(snapshot.Crop);

                // 对齐结果与当时的裁剪区域配套，恢复该裁剪后再重新挂接
                if (!ReferenceEquals(_sourceOverride, snapshot.SourceOverride))
                {
                    _sourceOverride = snapshot.SourceOverride;
                    OnPropertyChanged(nameof(HasSourceOverride));
                    OnPropertyChanged(nameof(SourceOverrideHint));

                    if (_sourceOverride != null)
                    {
                        RebuildPreviewPipeline(_sourceOverride);
                        UpdateHighResolutionOptions(_sourceOverride);
                        InvalidateProcessedCaches();
                    }
                }

                _cornerRadiusPixels = snapshot.CornerRadiusPixels;
                _cornerRadiusPercent = snapshot.CornerRadiusPercent;
                CornerRadiusUnit = snapshot.RadiusUnit;
                SelectedMaskShape = snapshot.Shape;
                CircleFitMode = snapshot.CircleFitMode;
                CirclePaddingPercent = snapshot.CirclePaddingPercent;
                ExpandCanvasPixels = snapshot.ExpandCanvasPixels;
                CanvasFillMode = snapshot.CanvasFillMode;
                CanvasCustomColorHex = snapshot.CanvasCustomColorHex;
                FillCanvasOutsideMask = snapshot.FillCanvasOutsideMask;
                ImageScale = snapshot.ImageScale;
                ImageOffsetX = snapshot.ImageOffsetX;
                ImageOffsetY = snapshot.ImageOffsetY;

                if (WorkingSource != null)
                {
                    UpdateCornerRadiusMaximum(WorkingSource);
                    UpdateImageTransformLimits(WorkingSource);
                }
            }
            finally
            {
                _isRestoringState = false;
                _suspendUndoCapture = false;
            }

            AddLog($"{logPrefix}: {DescribeMaskState(snapshot)}");
            AddHistoryEntry($"{logPrefix} - {DescribeMaskState(snapshot)}");
            OnPropertyChanged(nameof(CornerRadius));
            OnPropertyChanged(nameof(CornerRadiusHint));
            UpdateRadiusPresetSelection();
            SchedulePreviewUpdate();
            NotifyCommandStateChanges();
        }

        private string DescribeMaskState(MaskStateSnapshot snapshot)
        {
            var radiusDescription = snapshot.RadiusUnit == RadiusUnit.Percent
                ? $"R={snapshot.CornerRadiusPercent}%"
                : $"R={snapshot.CornerRadiusPixels}px";

            var circleDescription = snapshot.Shape == MaskShape.Circle
                ? $" · 圆形={snapshot.CircleFitMode}" + (snapshot.CircleFitMode == CircleFitMode.Padded ? $"({snapshot.CirclePaddingPercent}%)" : string.Empty)
                : string.Empty;

            var canvasDescription = snapshot.ExpandCanvasPixels switch
            {
                > 0 => $" · 画布+{snapshot.ExpandCanvasPixels}px({snapshot.CanvasFillMode}" + (snapshot.CanvasFillMode == CanvasFillMode.Custom ? $":{snapshot.CanvasCustomColorHex}" : string.Empty) + ")",
                < 0 => $" · 画布-{Math.Abs(snapshot.ExpandCanvasPixels)}px",
                _ => string.Empty
            };

            if (snapshot.FillCanvasOutsideMask && snapshot.CanvasFillMode != CanvasFillMode.Transparent)
            {
                canvasDescription += " · 底色铺满";
            }

            var cropDescription = snapshot.Crop is { } crop && _originalImage != null
                && (crop.Width < _originalImage.PixelWidth || crop.Height < _originalImage.PixelHeight)
                ? $" · 裁剪 {crop.Width}×{crop.Height}"
                : string.Empty;

            return $"{snapshot.Shape} · {radiusDescription}{circleDescription}{canvasDescription}{cropDescription} · 缩放={snapshot.ImageScale:0.##}x · X={snapshot.ImageOffsetX:0.#}px · Y={snapshot.ImageOffsetY:0.#}px";
        }

        private void AddHistoryEntry(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            MaskHistory.Insert(0, message);
            const int historyLimit = 20;
            while (MaskHistory.Count > historyLimit)
            {
                MaskHistory.RemoveAt(MaskHistory.Count - 1);
            }
            NotifyCommandStateChanges();
        }

        private void ResetHistory()
        {
            MaskHistory.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            NotifyCommandStateChanges();
        }

        private readonly record struct MaskStateSnapshot(
            int CornerRadiusPixels,
            int CornerRadiusPercent,
            RadiusUnit RadiusUnit,
            MaskShape Shape,
            CircleFitMode CircleFitMode,
            int CirclePaddingPercent,
            int ExpandCanvasPixels,
            CanvasFillMode CanvasFillMode,
            string CanvasCustomColorHex,
            bool FillCanvasOutsideMask,
            double ImageScale,
            double ImageOffsetX,
            double ImageOffsetY,
            CropRegion? Crop,
            BitmapSource? SourceOverride);

    }
}
