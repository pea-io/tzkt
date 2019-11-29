﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Tzkt.Api.Models
{
    public class ProposalOperation : IOperation
    {
        public string Type => "proposal";

        public int Id { get; set; }

        public int Level { get; set; }

        public DateTime Timestamp { get; set; }

        public string Hash { get; set; }

        public PeriodInfo Period { get; set; }

        public string Proposal { get; set; }

        public Alias Delegate { get; set; }
    }
}