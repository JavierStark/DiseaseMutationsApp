﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiseaseMutationsApp.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiseaseMutationsApp.Pages
{
    public partial class Index : ComponentBase, IDisposable
    {
        [Inject] private IJSRuntime JSRuntime { get; set; } = default!;
        [Inject] private GrnaService GrnaService { get; set; } = default!;
        [Inject] private NavigationManager Nav { get; set; } = default!;
        [Inject] private AppStateService StateService { get; set; } = default!;

        // Use properties that bind to the state service
        private string? _hgvs
        {
            get => StateService.IndexHgvsInput;
            set => StateService.IndexHgvsInput = value;
        }

        private int _gRnaSize
        {
            get => StateService.IndexGRnaSize;
            set => StateService.IndexGRnaSize = value;
        }

        private List<InputTabData> _inputTabs => StateService.IndexInputTabs;
        
        private int _activeTabIndex
        {
            get => StateService.IndexActiveTabIndex;
            set => StateService.IndexActiveTabIndex = value;
        }
        
        private Dictionary<int, int> _activeChildTabIndices => StateService.IndexActiveChildTabIndices;

        // Helper to get sorted gRNA list for display
        private IEnumerable<GRNAResult> GetSortedGRNAs(HgvsData hgvsData)
        {
            if (hgvsData.GRNAs == null || hgvsData.GRNAs.Count == 0)
                return Enumerable.Empty<GRNAResult>();

            var asc = hgvsData.SortAscending;
            return hgvsData.SortColumn switch
            {
                GrnaSortColumn.Sequence => asc ? hgvsData.GRNAs.OrderBy(g => g.Sequence) : hgvsData.GRNAs.OrderByDescending(g => g.Sequence),
                GrnaSortColumn.GCScore => asc ? hgvsData.GRNAs.OrderBy(g => g.GCScore) : hgvsData.GRNAs.OrderByDescending(g => g.GCScore),
                GrnaSortColumn.HomopolymerCount => asc ? hgvsData.GRNAs.OrderBy(g => g.HomopolymerCount) : hgvsData.GRNAs.OrderByDescending(g => g.HomopolymerCount),
                GrnaSortColumn.SeedRegion => asc ? hgvsData.GRNAs.OrderBy(g => g.SeedRegion) : hgvsData.GRNAs.OrderByDescending(g => g.SeedRegion),
                GrnaSortColumn.Alignments => asc ? hgvsData.GRNAs.OrderBy(g => g.Allignments) : hgvsData.GRNAs.OrderByDescending(g => g.Allignments),
                GrnaSortColumn.Energy => asc ? hgvsData.GRNAs.OrderBy(g => g.RnaFoldResult.Energy) : hgvsData.GRNAs.OrderByDescending(g => g.RnaFoldResult.Energy),
                GrnaSortColumn.Score => asc ? hgvsData.GRNAs.OrderBy(g => g.Score) : hgvsData.GRNAs.OrderByDescending(g => g.Score),
                _ => hgvsData.GRNAs
            };
        }

        // Toggle sort state
        private void SortBy(HgvsData hgvsData, GrnaSortColumn column)
        {
            if (hgvsData.SortColumn == column)
            {
                hgvsData.SortAscending = !hgvsData.SortAscending;
            }
            else
            {
                hgvsData.SortColumn = column;
                hgvsData.SortAscending = true;
            }
        }

        private async Task FetchData()
        {
            if (!string.IsNullOrWhiteSpace(_hgvs) && _gRnaSize > 0)
            {
                _inputTabs.Clear();
                _activeTabIndex = 0;
                _activeChildTabIndices.Clear();

                try
                {
                    var inputs = _hgvs.Split(',')
                        .Select(h => h.Trim())
                        .Where(h => !string.IsNullOrWhiteSpace(h))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (inputs.Count == 0) return;

                    // Parse inputs and create tabs
                    foreach (var input in inputs)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(input, @"^rs\d+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            // RS code
                            var rsIdStr = input[2..];
                            _inputTabs.Add(new InputTabData
                            {
                                Type = InputType.RS,
                                DisplayLabel = input.ToLower(),
                                RsId = rsIdStr,
                                IsLoading = true
                            });
                        }
                        else
                        {
                            // HGVS code
                            _inputTabs.Add(new InputTabData
                            {
                                Type = InputType.HGVS,
                                DisplayLabel = input,
                                IsLoading = true,
                                DirectHgvs = new HgvsData
                                {
                                    Hgvs = input,
                                    IsLoading = true
                                }
                            });
                        }
                    }

                    StateHasChanged();

                    // Fetch data for each tab
                    for (var i = 0; i < _inputTabs.Count; i++)
                    {
                        await FetchInputTabDataAsync(_inputTabs[i], i);
                    }

                }
                catch (Exception ex)
                {
                    if (_inputTabs.Any())
                    {
                        _inputTabs[0].ErrorMessage = $"Error: {ex.Message}";
                        _inputTabs[0].IsLoading = false;
                    }
                }
            }
        }

        private async Task FetchInputTabDataAsync(InputTabData tabData, int tabIndex)
        {
            try
            {
                switch (tabData.Type)
                {
                    case InputType.RS when tabData.RsId != null:
                    {
                        // Fetch HGVS list from RS
                        var hgvsList = await GrnaService.GetHgvsFromSnp(tabData.RsId);
                        Console.WriteLine($"RS{tabData.RsId} returned {hgvsList.Count} HGVS variants.");

                        // Create child HGVS tabs with loading state
                        tabData.ChildHgvsList = hgvsList.Select(h => new HgvsData
                        {
                            Hgvs = h,
                            IsLoading = true
                        }).ToList();

                        // Initialize active child tab for this parent
                        if (tabData.ChildHgvsList.Any())
                        {
                            _activeChildTabIndices[tabIndex] = 0;
                        }

                        // Mark parent as no longer loading so tabs appear immediately
                        tabData.IsLoading = false;

                        await InvokeAsync(StateHasChanged);

                        // Fetch data for each child HGVS
                        foreach (var childHgvs in tabData.ChildHgvsList)
                        {
                            await FetchHgvsDataAsync(childHgvs);
                        }

                        break;
                    }
                    case InputType.HGVS when tabData.DirectHgvs != null:
                        // Fetch data for direct HGVS
                        await FetchHgvsDataAsync(tabData.DirectHgvs);

                        // Sync parent tab status with child HGVS status
                        if (tabData.DirectHgvs.ErrorMessage != null)
                        {
                            tabData.ErrorMessage = tabData.DirectHgvs.ErrorMessage;
                        }
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            catch (Exception ex)
            {
                tabData.ErrorMessage = $"Error fetching data: {ex.Message}";
                Console.WriteLine($"Error for {tabData.DisplayLabel}: {ex}");
            }
            finally
            {
                tabData.IsLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task FetchHgvsDataAsync(HgvsData hgvsData)
        {
            try
            {
                var result = await GrnaService.GetBestgRNAFromHgvs(hgvsData.Hgvs, _gRnaSize);

                hgvsData.Original = result.OriginalSequence;
                hgvsData.Mutated = result.MutatedSequence;
                hgvsData.GRNAs = result.gRNA;
                var extraNucleotids = result.ExtraNucleotids;

                if (!string.IsNullOrEmpty(hgvsData.Original) && extraNucleotids >= 0 && extraNucleotids < hgvsData.Original.Length)
                {
                    hgvsData.Original = hgvsData.Original.Insert(extraNucleotids, "<b>");
                    if (hgvsData.Original.Length > extraNucleotids)
                    {
                        hgvsData.Original = hgvsData.Original.Insert(hgvsData.Original.Length - extraNucleotids, "</b>");
                    }
                }

                if (!string.IsNullOrEmpty(hgvsData.Mutated) && extraNucleotids >= 0 && extraNucleotids < hgvsData.Mutated.Length)
                {
                    hgvsData.Mutated = hgvsData.Mutated.Insert(extraNucleotids, "<b>");
                    if (hgvsData.Mutated.Length > extraNucleotids)
                    {
                        hgvsData.Mutated = hgvsData.Mutated.Insert(hgvsData.Mutated.Length - extraNucleotids, "</b>");
                    }
                }
            }
            catch (Exception ex)
            {
                hgvsData.ErrorMessage = $"Error fetching data: {ex.Message}";
                Console.WriteLine($"Error for {hgvsData.Hgvs}: {ex}");
            }
            finally
            {
                hgvsData.IsLoading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private void HandlegRNACreationButton(string spacer, HgvsData hgvsData)
        {
            if (hgvsData == null) return;

            hgvsData.SelectedSpacer = spacer;
            hgvsData.CopiedToClipboard = false;
        }

        private async Task CopyCompleteGRNA(HgvsData hgvsData)
        {
            if (hgvsData?.SelectedSpacer != null)
            {
                hgvsData.CopiedToClipboard = true;
                StateHasChanged();
                var completeGRNA = "GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC" + hgvsData.SelectedSpacer;

                await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", completeGRNA);
            }
        }

        private async Task GetRnaFolding(HgvsData hgvsData)
        {
            if (hgvsData?.SelectedSpacer == null) return;

            try
            {
                hgvsData.IsLoadingRnaFold = true;
                hgvsData.RnaFoldError = null;
                hgvsData.RnaFoldResult = null;
                hgvsData.FornaUrl = null;
                StateHasChanged();

                var completeGRNA = "GAUUUAGACUACCCCAAAAACGAAGGGGACUAAAAC" + hgvsData.SelectedSpacer;
                var result = await GrnaService.GetRnaFold(completeGRNA);

                hgvsData.RnaFoldResult = result;

                // Get the Forna URL
                try
                {
                    hgvsData.FornaUrl = GrnaService.GetFornaUrl(completeGRNA, result.Structure);

                }
                catch (Exception urlEx)
                {
                    Console.WriteLine($"Error getting Forna URL: {urlEx}");
                    // Continue even if URL fails - we still have the text structure
                }
            }
            catch (Exception ex)
            {
                hgvsData.RnaFoldError = ex.Message;
                Console.WriteLine($"Error getting RNA folding: {ex}");
            }
            finally
            {
                hgvsData.IsLoadingRnaFold = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        protected override async Task OnInitializedAsync()
        {
            // Subscribe to state changes
            StateService.OnStateChanged += StateHasChanged;

            // Subscribe to navigation changes
            Nav.LocationChanged += OnLocationChanged;

            // Check for rs query parameter and auto-populate
            await ProcessUrlParameters();
        }

        protected override async Task OnParametersSetAsync()
        {
            // Handle URL parameter changes
            await ProcessUrlParameters();
        }

        private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
        {
            // Handle navigation changes
            InvokeAsync(async () =>
            {
                await ProcessUrlParameters();
                StateHasChanged();
            });
        }

        private async Task ProcessUrlParameters()
        {
            try
            {
                var uri = new Uri(Nav.Uri);
                var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                var rsParam = query["rs"];
                
                if (!string.IsNullOrWhiteSpace(rsParam))
                {
                    var trimmedParam = rsParam.Trim();
                    // Update input field if parameter is different from current value
                    if (_hgvs != trimmedParam)
                    {
                        _hgvs = trimmedParam;
                        // Auto-fetch data when coming from navigation
                        await FetchData();
                    }
                }
            }
            catch
            {
                // ignore parsing errors
            }
        }

        public void Dispose()
        {
            // Unsubscribe from state changes to prevent memory leaks
            StateService.OnStateChanged -= StateHasChanged;
            Nav.LocationChanged -= OnLocationChanged;
        }
    }
}

