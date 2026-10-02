using e600ShopApi.Data;
using e600ShopApi.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace e600ShopApi.Tests;

/// <summary>
/// Verifies the EF Core model and relationships. Building the model does not open a
/// database connection, so these tests run without PostgreSQL.
/// </summary>
public class AppDbContextTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=e600shop_tests;Username=postgres;Password=not-used")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void Model_Contains_All_Domain_Entities()
    {
        using var context = CreateContext();

        var entityTypes = context.Model.GetEntityTypes().Select(entity => entity.ClrType).ToArray();

        Assert.Contains(typeof(Product), entityTypes);
        Assert.Contains(typeof(User), entityTypes);
        Assert.Contains(typeof(Order), entityTypes);
        Assert.Contains(typeof(OrderItem), entityTypes);
        Assert.Contains(typeof(Address), entityTypes);
    }

    [Fact]
    public void Order_Restricts_User_Delete_And_Cascades_Items()
    {
        using var context = CreateContext();

        var order = context.Model.FindEntityType(typeof(Order))!;
        var orderToUser = order.FindNavigation(nameof(Order.User))!.ForeignKey;
        Assert.Equal(DeleteBehavior.Restrict, orderToUser.DeleteBehavior);

        var orderItem = context.Model.FindEntityType(typeof(OrderItem))!;
        var itemToOrder = orderItem.FindNavigation(nameof(OrderItem.Order))!.ForeignKey;
        Assert.Equal(DeleteBehavior.Cascade, itemToOrder.DeleteBehavior);

        var itemToProduct = orderItem.FindNavigation(nameof(OrderItem.Product))!.ForeignKey;
        Assert.Equal(DeleteBehavior.Restrict, itemToProduct.DeleteBehavior);
    }

    [Fact]
    public void Address_Cascades_From_User()
    {
        using var context = CreateContext();

        var address = context.Model.FindEntityType(typeof(Address))!;
        var addressToUser = address.FindNavigation(nameof(Address.User))!.ForeignKey;

        Assert.Equal(DeleteBehavior.Cascade, addressToUser.DeleteBehavior);
    }

    [Fact]
    public void Money_Properties_Use_Precision_18_2()
    {
        using var context = CreateContext();

        AssertPrecision(context.Model.FindEntityType(typeof(Product))!, nameof(Product.Price));
        AssertPrecision(context.Model.FindEntityType(typeof(Order))!, nameof(Order.TotalAmount));
        AssertPrecision(context.Model.FindEntityType(typeof(OrderItem))!, nameof(OrderItem.UnitPrice));
        AssertPrecision(context.Model.FindEntityType(typeof(OrderItem))!, nameof(OrderItem.Subtotal));
    }

    [Fact]
    public void User_Email_Is_Unique_And_GoogleSubjectId_Is_Unique()
    {
        using var context = CreateContext();

        var user = context.Model.FindEntityType(typeof(User))!;

        var emailIndex = user.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(User.Email)));
        var googleIndex = user.GetIndexes()
            .Single(index => index.Properties.Any(property => property.Name == nameof(User.GoogleSubjectId)));

        Assert.True(emailIndex.IsUnique);
        Assert.True(googleIndex.IsUnique);
    }

    [Fact]
    public void Order_Status_Is_Stored_As_String()
    {
        using var context = CreateContext();

        var order = context.Model.FindEntityType(typeof(Order))!;
        var status = order.FindProperty(nameof(Order.Status))!;

        Assert.Equal("character varying(32)", status.GetColumnType());
        Assert.Equal(typeof(OrderStatus), status.ClrType);
        Assert.Equal(typeof(string), status.GetProviderClrType());
    }

    private static void AssertPrecision(IEntityType entityType, string propertyName)
    {
        var property = entityType.FindProperty(propertyName)!;
        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
    }
}
