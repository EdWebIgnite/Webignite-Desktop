using Webignite.Desktop.Controls;

namespace Webignite.Desktop.Tests;

public class ColumnLayoutTests
{
    [Fact]
    public void With_room_for_every_group_each_has_its_own_column()
    {
        double[] groups = [100, 300, 200];
        Assert.Equal(300, ColumnLayout.ShortestHeight(groups, 5, 10));
        Assert.Equal([0, 1, 2], ColumnLayout.Place(groups, 300, 10).Select(p => p.Column));
    }

    [Fact]
    public void With_fewer_columns_short_groups_share_one_and_the_order_is_kept()
    {
        double[] groups = [100, 100, 300, 120, 120];
        var height = ColumnLayout.ShortestHeight(groups, 3, 10);
        Assert.Equal(300, height);
        var places = ColumnLayout.Place(groups, height, 10);
        Assert.Equal([0, 0, 1, 2, 2], places.Select(p => p.Column));
        Assert.Equal(110, places[1].Top);
    }

    [Fact]
    public void One_column_stacks_everything()
    {
        double[] groups = [100, 200, 50];
        Assert.Equal(370, ColumnLayout.ShortestHeight(groups, 1, 10));
    }

    [Fact]
    public void Nothing_to_show_takes_no_room()
    {
        Assert.Equal(0, ColumnLayout.ShortestHeight([], 3, 10));
        Assert.Empty(ColumnLayout.Place([], 0, 10));
    }
}
