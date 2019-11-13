﻿using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Tzkt.Data.Models
{
    public class Delegate : User
    {
        public int ActivationLevel { get; set; }
        public int DeactivationLevel { get; set; }

        public long FrozenDeposits { get; set; }
        public long FrozenRewards { get; set; }
        public long FrozenFees { get; set; }

        public int Delegators { get; set; }
        public long StakingBalance { get; set; }

        #region indirect relations
        public List<Account> DelegatedAccounts { get; set; }
        #endregion
    }

    public static class DelegateModel
    {
        public static void BuildDelegateModel(this ModelBuilder modelBuilder)
        {
        }
    }
}
