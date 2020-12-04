using System;

namespace DSoft.EntityFrameworkCore.GDPR
{
    [AttributeUsage(AttributeTargets.Property)]
    public class PrivatePropertyAttribute : Attribute
    {
        private PrivacyCategory _category;

        public PrivatePropertyAttribute(PrivacyCategory category)
        {
            _category = category;
        }
    }
}
