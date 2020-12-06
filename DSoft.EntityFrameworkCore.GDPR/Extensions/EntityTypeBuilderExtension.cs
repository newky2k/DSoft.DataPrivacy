using DSoft.EntityFrameworkCore.GDPR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public static class EntityTypeBuilderExtension
    {
        sealed class ReferencedPropertyFinder : ExpressionVisitor
        {
            private readonly Type _ownerType;
            private readonly List<PropertyInfo> _properties = new List<PropertyInfo>();

            public ReferencedPropertyFinder(Type ownerType)
            {
                _ownerType = ownerType;
            }

            public IReadOnlyList<PropertyInfo> Properties
            {
                get { return _properties; }
            }

            protected override Expression VisitMember(MemberExpression node)
            {
                var propertyInfo = node.Member as PropertyInfo;
                if (propertyInfo != null && _ownerType.IsAssignableFrom(propertyInfo.DeclaringType))
                {
                    // probably more filtering required
                    _properties.Add(propertyInfo);
                }
                return base.VisitMember(node);
            }
        }

        private static IReadOnlyList<PropertyInfo> GetReferencedProperties<T, U>(Expression<Func<T, U>> expression)
        {
            var v = new ReferencedPropertyFinder(typeof(T));
            v.Visit(expression);
            return v.Properties;
        }

        public static EntityTypeBuilder<TEntity> IsPrivateRecord<TEntity>(this EntityTypeBuilder<TEntity> builder, Expression<Func<TEntity, object>> keyExpression) where TEntity : class
        {
            var props = GetReferencedProperties(keyExpression);

            if (props.Any())
            {
                var entityType = builder.Metadata.ClrType.GetTypeInfo();

                var idProp = props.First();

                var privateRecordAtrribute = entityType.GetCustomAttribute<PrivateRecordAttribute>();

                if (privateRecordAtrribute == null)
                {
                    //not attached so add a new attribute
                }
            }

            return builder;
        }

    }
}
