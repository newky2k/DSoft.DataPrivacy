using DSoft.EntityFrameworkCore.GDPR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GDPRCore.Models
{
    public class Address
    {
        public int Id { get; set; }

        public int PersonId { get; set; }

        [ParentRecordProperty(nameof(PersonId))]
        public virtual Person Person { get; set; }

        [PrivateProperty(PrivacyCategory.Private)]
        public string EmailAddress { get; set; }

    }
}
