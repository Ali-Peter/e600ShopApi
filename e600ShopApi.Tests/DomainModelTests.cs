using e600ShopApi.Domain;

namespace e600ShopApi.Tests;

public class DomainModelTests
{
    [Fact]
    public void Product_Defaults_UtcNow_CreatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var product = new Product();

        var after = DateTime.UtcNow.AddSeconds(1);
        Assert.InRange(product.CreatedAt, before, after);
        Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
        Assert.Equal(0, product.StockQuantity);
    }

    [Fact]
    public void User_Defaults_UtcNow_CreatedAt()
    {
        var user = new User
        {
            Email = "ada@example.com",
            FirstName = "Ada",
            LastName = "Obi",
        };

        Assert.Equal(DateTimeKind.Utc, user.CreatedAt.Kind);
        Assert.Null(user.GoogleSubjectId);
        Assert.Empty(user.Orders);
        Assert.Empty(user.Addresses);
    }

    [Fact]
    public void Order_Defaults_ToPending_With_EmptyItems()
    {
        var order = new Order { UserId = Guid.NewGuid(), TotalAmount = 99.90m };

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Empty(order.Items);
        Assert.Equal(DateTimeKind.Utc, order.CreatedAt.Kind);
    }

    [Fact]
    public void OrderItem_Stores_Quantity_UnitPrice_And_Subtotal()
    {
        var orderItem = new OrderItem
        {
            OrderId = Guid.NewGuid(),
            ProductId = Guid.NewGuid(),
            Quantity = 3,
            UnitPrice = 19.99m,
            Subtotal = 59.97m,
        };

        Assert.Equal(3, orderItem.Quantity);
        Assert.Equal(19.99m, orderItem.UnitPrice);
        Assert.Equal(59.97m, orderItem.Subtotal);
    }

    [Fact]
    public void Address_Holds_All_Delivery_Fields()
    {
        var address = new Address
        {
            UserId = Guid.NewGuid(),
            FullName = "Ada Obi",
            AddressLine = "12 Admiralty Way",
            City = "Lagos",
            State = "Lagos",
            Country = "Nigeria",
            PhoneNumber = "+2348012345678",
        };

        Assert.Equal("Ada Obi", address.FullName);
        Assert.Equal("12 Admiralty Way", address.AddressLine);
        Assert.Equal("Lagos", address.City);
        Assert.Equal("Lagos", address.State);
        Assert.Equal("Nigeria", address.Country);
        Assert.Equal("+2348012345678", address.PhoneNumber);
    }
}
