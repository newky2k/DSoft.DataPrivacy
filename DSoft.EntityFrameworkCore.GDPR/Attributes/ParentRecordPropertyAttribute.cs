using System;
using System.Collections.Generic;
using System.Text;

namespace DSoft.EntityFrameworkCore.GDPR
{
    [AttributeUsage(AttributeTargets.Property)]
    public class ParentRecordPropertyAttribute : Attribute
    {
        private string _foreignKeyPropertyName;

        public ParentRecordPropertyAttribute(string foreignKeyPropertyName)
        {
            _foreignKeyPropertyName = foreignKeyPropertyName;
        }
    }
}
