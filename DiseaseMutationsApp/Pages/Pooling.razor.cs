using System.Text;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiseaseMutationsApp.Pages
{
    public partial class Pooling : ComponentBase, IDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private PoolingService PoolingService { get; set; } = default!;
        [Inject] private AppStateService StateService { get; set; } = default!;

        // ===== State proxies onto AppStateService so the plan survives navigation =====

        private PoolingInputMode _inputMode
        {
            get => StateService.PoolingInputMode;
            set => StateService.PoolingInputMode = value;
        }

        private int _guideCount
        {
            get => StateService.PoolingGuideCount;
            set => StateService.PoolingGuideCount = value;
        }

        /// <summary>
        /// Setting the raw text re-parses immediately, so the preview and the guide count stay
        /// in step with what was pasted without waiting for the Calculate button.
        /// </summary>
        private string? _guideListText
        {
            get => StateService.PoolingGuideListText;
            set
            {
                StateService.PoolingGuideListText = value;
                var parsed = GuideListParser.Parse(value);
                StateService.PoolingParsedGuides = parsed.Guides;
                StateService.PoolingParseWarnings = parsed.Warnings;
                StateService.PoolingGuideListSource = parsed.Source;
            }
        }

        private List<GuideEntry> _parsedGuides => StateService.PoolingParsedGuides;
        private List<string> _parseWarnings => StateService.PoolingParseWarnings;
        private GuideListSource _guideListSource => StateService.PoolingGuideListSource;

        private int _wellCapacity
        {
            get => StateService.PoolingWellCapacity;
            set => StateService.PoolingWellCapacity = value;
        }

        private PlateKind _plate
        {
            get => StateService.PoolingPlate;
            set => StateService.PoolingPlate = value;
        }

        private PoolingModelKind? _selectedModel
        {
            get => StateService.PoolingSelectedModel;
            set => StateService.PoolingSelectedModel = value;
        }

        private List<PoolingModelEstimate>? _estimates
        {
            get => StateService.PoolingEstimates;
            set => StateService.PoolingEstimates = value;
        }

        private PoolingPlanDto? _plan
        {
            get => StateService.PoolingPlan;
            set => StateService.PoolingPlan = value;
        }

        private int _activePlate
        {
            get => StateService.PoolingActivePlate;
            set => StateService.PoolingActivePlate = value;
        }

        private int? _selectedPoolId
        {
            get => StateService.PoolingSelectedPoolId;
            set => StateService.PoolingSelectedPoolId = value;
        }

        private string? _errorMessage
        {
            get => StateService.PoolingErrorMessage;
            set => StateService.PoolingErrorMessage = value;
        }

        // ===== Derived values =====

        /// <summary>V: how many guides the plan is for, from whichever input mode is active.</summary>
        private int EffectiveGuideCount =>
            _inputMode == PoolingInputMode.GuideCount ? _guideCount : _parsedGuides.Count;

        private bool CanCalculate =>
            EffectiveGuideCount >= 1
            && EffectiveGuideCount <= PoolingService.MaxGuideCount
            && _wellCapacity >= 1;

        private PoolWell? SelectedPool =>
            _selectedPoolId is { } id ? _plan?.Pools.FirstOrDefault(p => p.Id == id) : null;

        protected override void OnInitialized()
        {
            StateService.OnStateChanged += StateHasChanged;
        }

        // ===== Actions =====

        private void SetInputMode(PoolingInputMode mode)
        {
            if (_inputMode == mode) return;

            _inputMode = mode;
            // The guide count changes meaning between modes, so any existing plan is stale.
            ClearResults();
        }

        private void Calculate()
        {
            _errorMessage = null;

            try
            {
                var v = EffectiveGuideCount;
                var k = _wellCapacity;

                _estimates = PoolingService.CompareModels(v, k);

                // Honour a manual override, otherwise take the cheapest model.
                var model = _selectedModel ?? _estimates[0].Model;
                BuildPlan(model, v, k);
            }
            catch (ArgumentException ex)
            {
                ClearResults();
                _errorMessage = ex.Message;
            }
            catch (Exception ex)
            {
                ClearResults();
                _errorMessage = $"Could not build the pooling plan: {ex.Message}";
            }
        }

        private void SelectModel(PoolingModelKind model)
        {
            if (_plan is null) return;

            _selectedModel = model;

            try
            {
                BuildPlan(model, EffectiveGuideCount, _wellCapacity);
                _errorMessage = null;
            }
            catch (Exception ex)
            {
                _errorMessage = $"Could not switch to the {model} layout: {ex.Message}";
            }
        }

        /// <summary>Drops the manual override and goes back to whichever model is cheapest.</summary>
        private void UseOptimalModel()
        {
            _selectedModel = null;
            Calculate();
        }

        private void BuildPlan(PoolingModelKind model, int v, int k)
        {
            var guides = _inputMode == PoolingInputMode.GuideList && _parsedGuides.Count > 0
                ? _parsedGuides
                : null;

            _plan = PoolingService.BuildPlan(model, v, k, _plate, guides);
            _activePlate = 1;
            _selectedPoolId = null;
        }

        /// <summary>
        /// The plate format only changes where pools sit, not what is in them, so re-address an
        /// existing plan straight away rather than making the user press Calculate again.
        /// </summary>
        private void OnPlateChanged()
        {
            if (_plan is null) return;

            try
            {
                BuildPlan(_plan.Model, EffectiveGuideCount, _wellCapacity);
                _errorMessage = null;
            }
            catch (Exception ex)
            {
                _errorMessage = $"Could not re-address the plan: {ex.Message}";
            }
        }

        private void SelectPool(PoolWell pool)
        {
            _selectedPoolId = _selectedPoolId == pool.Id ? null : pool.Id;
        }

        private void ShowPlate(int plate)
        {
            _activePlate = plate;
            _selectedPoolId = null;
        }

        private void ClearResults()
        {
            _estimates = null;
            _plan = null;
            _activePlate = 1;
            _selectedPoolId = null;
        }

        private void ClearAll()
        {
            StateService.ClearPoolingState();
        }

        // ===== Export =====

        private async Task DownloadPlan()
        {
            if (_plan is null) return;

            var sb = new StringBuilder();
            sb.AppendLine("Tube ID,Mixture Name,Guides in Tandem,Well");

            foreach (var pool in _plan.Pools)
            {
                var contents = pool.IsEmpty
                    ? "Empty - do not prepare"
                    : string.Join("; ", pool.GuideLabels);

                sb.AppendLine(string.Join(",", Csv(pool.Id.ToString()), Csv(pool.Name), Csv(contents), Csv(pool.WellLabel)));
            }

            var fileName = $"Pooling_{_plan.ModelName.Replace(" ", "")}_V{_plan.GuideCount}_K{_plan.WellCapacity}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            await JSRuntime.InvokeVoidAsync("downloadFile", fileName, "text/csv;charset=utf-8", sb.ToString());
        }

        /// <summary>
        /// Guide labels are pasted by the user and may contain commas or quotes, so fields are
        /// quoted properly rather than concatenated raw.
        /// </summary>
        private static string Csv(string value)
        {
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        public void Dispose()
        {
            StateService.OnStateChanged -= StateHasChanged;
        }
    }
}
