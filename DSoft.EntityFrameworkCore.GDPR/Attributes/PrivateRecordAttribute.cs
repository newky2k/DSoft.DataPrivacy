using System;
using System.Collections.Generic;
using System.Text;

namespace DSoft.EntityFrameworkCore.GDPR
{
    [AttributeUsage(AttributeTargets.Class)]
    public class PrivateRecordAttribute : Attribute
    {
        private string _identityPropertyName;

        public PrivateRecordAttribute(string identityPropertyName)
        {
            _identityPropertyName = identityPropertyName;
        }
    }
}
