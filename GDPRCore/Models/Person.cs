using DSoft.EntityFrameworkCore.GDPR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GDPRCore.Models
{
    [PrivateRecord(nameof(Id))]
    public class Person
    {
        public int Id { get; set; }

        [PrivateProperty(PrivacyCategory.Private)]
        public string Name { get; set; }
    }
}
