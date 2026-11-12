using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Webignite.Desktop.Views;

public partial class OverviewView : UserControl
{
    /// <summary>From this width Needs you and Today sit side by side instead of one above the other.</summary>
    private const double SideBySide = 1300;

    private bool? wide;

    public OverviewView()
    {
        InitializeComponent();
        Lower.SizeChanged += (_, _) => Arrange();
        DataContextChanged += (_, _) => Follow();
        NeedsCard.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty)
            {
                wide = null;
                Arrange();
            }
        };
    }

    private ViewModels.OverviewViewModel? following;
    private MyPagesView? board;

    // The person's own widgets are drawn by the view My pages uses, made once there is something to show and
    // let go with the page.
    private void Follow()
    {
        if (following is not null)
            following.PropertyChanged -= BoardChanged;
        following = DataContext as ViewModels.OverviewViewModel;
        if (following is not null)
            following.PropertyChanged += BoardChanged;
        ShowBoard();
    }

    private void BoardChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.OverviewViewModel.Board))
            ShowBoard();
    }

    private void ShowBoard()
    {
        var wanted = following?.Board;
        if (board is not null && !ReferenceEquals(board.DataContext, wanted))
        {
            Parts.Children.Remove(board);
            board = null;
        }
        if (wanted is not null && board is null)
        {
            board = new MyPagesView { DataContext = wanted };
            Parts.Children.Add(board);
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (following is not null)
            following.PropertyChanged -= BoardChanged;
        following = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Follow();
    }

    // The list of what shows on the page, and the way to add widgets, is made when Customise is clicked.
    private void Customise(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not ViewModels.OverviewViewModel overview)
            return;
        var flyout = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        var list = new StackPanel { Spacing = 4, MinWidth = 240 };
        Looks.PopUps(list);
        var heading = new TextBlock { Text = "Show on this page", Margin = new Thickness(6, 2, 6, 4) };
        heading.Classes.Add("label");
        heading.Classes.Add("caps");
        list.Children.Add(heading);
        void Pick(string words, bool on, System.Windows.Input.ICommand toggle)
        {
            var pick = new Avalonia.Controls.Primitives.ToggleButton { Content = words, IsChecked = on, Command = toggle };
            pick.Classes.Add("pick");
            list.Children.Add(pick);
        }
        Pick("The four figures", overview.FiguresOn, overview.ToggleFiguresCommand);
        Pick("Needs you", overview.NeedsYouOn, overview.ToggleNeedsYouCommand);
        Pick("Today", overview.TodayOn, overview.ToggleTodayCommand);
        if (overview.NoBoard)
        {
            var add = new Button { Content = "Add widgets of your own", Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
            add.Classes.Add("quiet");
            add.Click += (_, _) =>
            {
                flyout.Hide();
                overview.AddWidgetsCommand.Execute(null);
            };
            list.Children.Add(add);
        }
        var note = new TextBlock
        {
            Text = "Your widgets sit under what is ticked here. Add, move and size them with Add widget and Arrange.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 240, Margin = new Thickness(6, 6, 6, 0),
        };
        note.Classes.Add("muted");
        list.Children.Add(note);
        flyout.Content = list;
        flyout.ShowAt(button);
    }

    private void Arrange()
    {
        var nowWide = Lower.Bounds.Width >= SideBySide && NeedsCard.IsVisible;
        if (nowWide == wide)
            return;
        wide = nowWide;
        Lower.ColumnDefinitions = nowWide ? new ColumnDefinitions("3*,22,2*") : new ColumnDefinitions("*");
        Grid.SetColumn(TodayCard, nowWide ? 2 : 0);
        Grid.SetRow(TodayCard, nowWide ? 0 : 1);
        TodayCard.Margin = nowWide || !NeedsCard.IsVisible ? default : new Thickness(0, 22, 0, 0);
    }
}
