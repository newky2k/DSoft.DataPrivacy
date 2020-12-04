using DSoft.EntityFrameworkCore.GDPR;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public static class PropertyBuilderExtensions
    {
        public static PropertyBuilder<TProperty> IsPrivate<TProperty>(this PropertyBuilder<TProperty> builder, PrivacyCategory category)
        {
            return builder;
        }
    }
}
