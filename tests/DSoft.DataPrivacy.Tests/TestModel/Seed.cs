using System;
using System.Collections.Generic;

namespace DSoft.DataPrivacy.Tests.TestModel;

public static class Seed
{
    public static readonly DateTime Now = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A customer with an address, orders, consent, a clinical note and a support ticket.</summary>
    public static Customer FullCustomer(ShopContext context, string name = "Jo Bloggs", string email = "jo@example.com")
    {
        var customer = new Customer
        {
            Name = name,
            Email = email,
            DateOfBirth = new DateTime(1990, 5, 17),
            Notes = "Prefers morning deliveries; neighbour Sam takes parcels",
            PasswordHash = "AQAAAAEAACcQAAAAE-secret",
            HomeAddress = new Address { Line1 = "1 High Street", Postcode = "AB1 2CD" },
            LastActiveAt = Now.AddDays(-10),
            Orders = new List<Order>
            {
                new()
                {
                    DeliveryName = name,
                    Total = 42.50m,
                    PlacedAt = Now.AddDays(-30),
                    Lines = new List<OrderLine> { new() { Product = "Kettle", Quantity = 1 } },
                },
            },
        };

        context.Customers.Add(customer);
        context.SaveChanges();

        context.Consents.Add(new MarketingConsent { CustomerId = customer.Id, Channel = "Email", IpAddress = "203.0.113.7", GivenAt = Now.AddYears(-3) });
        context.ClinicalNotes.Add(new ClinicalNote { CustomerId = customer.Id, Text = "Allergic to penicillin", AuthorName = "Dr Smith", WrittenAt = Now.AddYears(-10) });
        context.Tickets.Add(new SupportTicket { RaisedById = customer.Id, Description = "Kettle arrived broken" });
        context.SaveChanges();

        return customer;
    }

    /// <summary>A customer with only a consent and a support ticket, so nothing forces them to be kept.</summary>
    public static Customer LightCustomer(ShopContext context, string name = "Alex Doe", string email = "alex@example.com")
    {
        var customer = new Customer { Name = name, Email = email, LastActiveAt = Now.AddYears(-5) };
        context.Customers.Add(customer);
        context.SaveChanges();

        context.Consents.Add(new MarketingConsent { CustomerId = customer.Id, Channel = "Sms", IpAddress = "198.51.100.4", GivenAt = Now.AddMonths(-1) });
        context.Tickets.Add(new SupportTicket { RaisedById = customer.Id, Description = "Where is my order?" });
        context.SaveChanges();

        return customer;
    }
}
