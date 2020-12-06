using System;
using DSoft.EntityFrameworkCore.GDPR.Models;
using Microsoft.EntityFrameworkCore;

namespace DSoft.EntityFrameworkCore.GDPR.Data
{
	public interface IGdprDbContext
	{
		DbSet<PrivateRecord> PrivateRecords { get; set; }


	}
}
