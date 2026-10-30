using System;
using System.Collections.Generic;
using System.Linq;
using PhoenixAdult.Helpers.Utils;
using Xunit;
using RouteDb = PhoenixAdult.Helpers.Utils.Database;

namespace Jellyfin.Plugin.PhoenixAdult.Tests;

/// <summary>
/// 番号路由的离线测试：规则来自 data/IdRoutes.json（这里注入等价的内存路由表），
/// 校验 prefix / label 正则的匹配优先级与站点解析。
/// </summary>
public class SearchRouterTests : IDisposable
{
    private static RouteDb.IdRoutesStructure CreateSampleRoutes() => new RouteDb.IdRoutesStructure
    {
        Routes = new List<RouteDb.IdRouteEntry>
        {
            new RouteDb.IdRouteEntry
            {
                Id = "madou",
                Note = "麻豆系",
                Labels = new[] { "mdx", "mdsr", "xjx", "jdsy" },
                LabelRegex = "^(?:MD|MTVQ)[A-Z0-9]*$",
                Sites = new[] { new[] { 234, 0 }, new[] { 96, 0 } },
            },
            new RouteDb.IdRouteEntry
            {
                Id = "fc2",
                Note = "FC2",
                IdPrefix = "FC2-PPV-",
                Sites = new[] { new[] { 238, 0 } },
            },
            new RouteDb.IdRouteEntry
            {
                Id = "jav",
                Note = "兜底",
                Sites = new[] { new[] { 239, 0 } },
            },
        },
    };

    public SearchRouterTests()
    {
        // Database 的静态构造依赖 Plugin.Instance，先让宿主环境就绪
        TestInitializer.Init();
        RouteDb.IdRoutes = CreateSampleRoutes();
    }

    public void Dispose()
    {
        RouteDb.IdRoutes = null;
    }

    [Theory]
    [InlineData("MDSR-0005", "madou", 234)]
    [InlineData("MDX-0267", "madou", 234)]
    [InlineData("XJX-0813", "madou", 234)]
    [InlineData("jdsy-0123", "madou", 96)]
    [InlineData("MTVQ18-EP1-AV", "madou", 96)]
    [InlineData("FC2-PPV-1234567", "fc2", 238)]
    [InlineData("ABP-123", "jav", 239)]
    [InlineData("SSIS-354", "jav", 239)]
    public void Ids_按路由表匹配(string id, string expectedFamily, int expectedGroup)
    {
        Assert.Equal(expectedFamily, SearchRouter.GetFamily(id));

        var sources = SearchRouter.GetSources(id);
        Assert.Contains(sources, s => s.Group == expectedGroup);
    }

    [Fact]
    public void 路由顺序即优先级_兜底不会抢走专属路由()
    {
        Assert.Equal("fc2", SearchRouter.GetFamily("FC2-PPV-4680562"));
        Assert.Equal("madou", SearchRouter.GetFamily("MDCM-0013"));
        Assert.Equal("jav", SearchRouter.GetFamily("SSIS-277"));
    }

    [Fact]
    public void 无路由表时返回空_不崩溃()
    {
        RouteDb.IdRoutes = null;

        Assert.Null(SearchRouter.GetFamily("ABP-123"));
        Assert.Empty(SearchRouter.GetSources("ABP-123"));
    }

    [Fact]
    public void 非法正则可以跳过而不抛异常()
    {
        RouteDb.IdRoutes = new RouteDb.IdRoutesStructure
        {
            Routes = new List<RouteDb.IdRouteEntry>
            {
                new RouteDb.IdRouteEntry { Id = "broken", LabelRegex = "([", Sites = new[] { new[] { 1, 1 } } },
                new RouteDb.IdRouteEntry { Id = "jav", Sites = new[] { new[] { 239, 0 } } },
            },
        };

        Assert.Equal("jav", SearchRouter.GetFamily("ABP-123"));
    }
}
