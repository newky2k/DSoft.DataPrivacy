using System;
namespace DSoft.EntityFrameworkCore.GDPR.Models
{
	public class PrivateRecord
	{
		public Guid Id { get; set; }

		public string TypeName { get; set; }

		public string PrimaryKeyPropertyName { get; set; }

		public PrivateRecord()
		{
		}
	}
}
