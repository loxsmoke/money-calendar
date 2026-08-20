using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace MoneyCalendar.ViewModels;

public enum PageState
{
    Loading = 0,
    Empty = 1,
    Error = 2,
    Content = 3,
}

/// <summary>
/// Base for all navigable pages. The loading state shows only on first load — background
/// refreshes never blank the UI, and a failed refresh over existing content surfaces as an
/// inline banner instead of a modal.
/// </summary>
public abstract partial class PageViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading), nameof(IsEmpty), nameof(IsError), nameof(IsContent), nameof(ShowErrorBanner))]
    private PageState _state = PageState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowErrorBanner))]
    private string? _errorMessage;

    public abstract string Title { get; }

    public bool IsLoading => State == PageState.Loading;
    public bool IsEmpty => State == PageState.Empty;
    public bool IsError => State == PageState.Error;
    public bool IsContent => State == PageState.Content;

    /// <summary>Refresh failed but stale content is still showing.</summary>
    public bool ShowErrorBanner => ErrorMessage is not null && State == PageState.Content;

    private bool _loadedOnce;

    public Task EnsureLoadedAsync() => _loadedOnce ? Task.CompletedTask : ReloadAsync();

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (!_loadedOnce)
            State = PageState.Loading;

        try
        {
            var hasContent = await LoadAsync(CancellationToken.None);
            _loadedOnce = true;
            ErrorMessage = null;
            State = hasContent ? PageState.Content : PageState.Empty;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Page load failed: {Page}", GetType().Name);
            ErrorMessage = "Something went wrong loading this view.";
            if (!_loadedOnce)
                State = PageState.Error;
        }
    }

    /// <summary>Loads (or reloads) page data. Returns false when there is nothing to show.</summary>
    protected abstract Task<bool> LoadAsync(CancellationToken ct);
}
