using DSoft.EntityFrameworkCore.GDPR;
using GDPRCore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GDPRCore.Data.Configuration
{
    public class PersonConfiguration : IEntityTypeConfiguration<Person>
    {
        public void Configure(EntityTypeBuilder<Person> builder)
        {
            builder.HasKey(x => x.Id);

            builder.IsPrivateRecord(x => x.Id);

            builder.Property(x => x.Name).IsPrivate(PrivacyCategory.Private);

            

        }
    }
}
