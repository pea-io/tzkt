﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Dapper;

using Tzkt.Api.Models;
using Tzkt.Api.Services.Cache;

namespace Tzkt.Api.Repositories
{
    public class AccountRepository : DbConnection
    {
        readonly AccountsCache Accounts;
        readonly StateCache State;
        readonly OperationRepository Operations;

        public AccountRepository(AccountsCache accounts, StateCache state, OperationRepository operations, IConfiguration config) : base(config)
        {
            Accounts = accounts;
            State = state;
            Operations = operations;
        }

        public async Task<IAccount> Get(string address)
        {
            var rawAccount = await Accounts.GetAsync(address);
            var metadata = Accounts.GetMetadata(rawAccount.Id);

            if (rawAccount == null)
                return address[0] == 't'
                    ? new EmptyAccount
                    {
                        Address = address,
                        Counter = State.GetCounter(),
                    }
                    : null;

            switch (rawAccount)
            {
                case RawDelegate delegat:
                    #region build delegate
                    return new Models.Delegate
                    {
                        Alias = metadata?.Alias,
                        Active = delegat.Staked,
                        Address = delegat.Address,
                        PublicKey = delegat.PublicKey,
                        Balance = delegat.Balance,
                        FrozenDeposits = delegat.FrozenDeposits,
                        FrozenRewards = delegat.FrozenRewards,
                        FrozenFees = delegat.FrozenFees,
                        Counter = delegat.Counter,
                        ActivationLevel = delegat.ActivationLevel,
                        DeactivationLevel = delegat.Staked ? null : (int?)delegat.DeactivationLevel,
                        StakingBalance = delegat.StakingBalance,
                        FirstActivity = delegat.FirstLevel,
                        LastActivity = delegat.LastLevel,
                        NumActivations = delegat.Activated == true ? 1 : 0,
                        NumBallots = delegat.BallotsCount,
                        NumContracts = delegat.Contracts,
                        NumDelegators = delegat.Delegators,
                        NumDelegations = delegat.DelegationsCount,
                        NumDoubleBaking = delegat.DoubleBakingCount,
                        NumDoubleEndorsing = delegat.DoubleEndorsingCount,
                        NumEndorsements = delegat.EndorsementsCount,
                        NumNonceRevelations = delegat.NonceRevelationsCount,
                        NumOriginations = delegat.OriginationsCount,
                        NumProposals = delegat.ProposalsCount,
                        NumReveals = delegat.RevealsCount,
                        NumSystem = delegat.SystemOpsCount,
                        NumTransactions = delegat.TransactionsCount,
                    };
                    #endregion
                case RawUser user:
                    #region build user
                    var userDelegate = user.DelegateId == null ? null
                        : await Accounts.GetAsync((int)user.DelegateId);

                    var userDelegateMetadata = userDelegate == null ? null
                        : Accounts.GetMetadata(userDelegate.Id);

                    return new User
                    {
                        Alias = metadata?.Alias,
                        Address = user.Address,
                        Balance = user.Balance,
                        Counter = user.Balance > 0 ? user.Counter : State.GetCounter(),
                        FirstActivity = user.FirstLevel,
                        LastActivity = user.LastLevel,
                        PublicKey = user.PublicKey,
                        Delegate = userDelegate == null ? null
                            : new DelegateInfo
                            {
                                Alias = userDelegateMetadata?.Alias,
                                Address = userDelegate.Address,
                                Active = userDelegate.Staked
                            },
                        NumActivations = user.Activated == true ? 1 : 0,
                        NumContracts = user.Contracts,
                        NumDelegations = user.DelegationsCount,
                        NumOriginations = user.OriginationsCount,
                        NumReveals = user.RevealsCount,
                        NumSystem = user.SystemOpsCount,
                        NumTransactions = user.TransactionsCount
                    };
                    #endregion
                case RawContract contract:
                    #region build contract
                    var creator = contract.CreatorId == null ? null
                        : await Accounts.GetAsync((int)contract.CreatorId);

                    var creatorMetadata = creator == null ? null
                        : Accounts.GetMetadata(creator.Id);

                    var manager = contract.ManagerId == null ? null
                        : (RawUser)await Accounts.GetAsync((int)contract.ManagerId);

                    var managerMetadata = manager == null ? null
                        : Accounts.GetMetadata(manager.Id);

                    var contractDelegate = contract.DelegateId == null ? null
                        : await Accounts.GetAsync((int)contract.DelegateId);

                    var contractDelegateMetadata = contractDelegate == null ? null
                        : Accounts.GetMetadata(contractDelegate.Id);

                    return new Contract
                    {
                        Alias = metadata?.Alias,
                        Address = contract.Address,
                        Kind = KindToString(contract.Kind),
                        Balance = contract.Balance,
                        Creator = creator == null ? null
                            : new CreatorInfo
                            {
                                Alias = creatorMetadata?.Alias,
                                Address = creator.Address
                            },
                        Manager = manager == null ? null
                            : new ManagerInfo
                            {
                                Alias = managerMetadata?.Alias,
                                Address = manager.Address,
                                PublicKey = manager.PublicKey,
                            },
                        Delegate = contractDelegate == null ? null
                            : new DelegateInfo
                            {
                                Alias = contractDelegateMetadata?.Alias,
                                Address = contractDelegate.Address,
                                Active = contractDelegate.Staked
                            },
                        FirstActivity = contract.FirstLevel,
                        LastActivity = contract.LastLevel,
                        NumContracts = contract.Contracts,
                        NumDelegations = contract.DelegationsCount,
                        NumOriginations = contract.OriginationsCount,
                        NumReveals = contract.RevealsCount,
                        NumSystem = contract.SystemOpsCount,
                        NumTransactions = contract.TransactionsCount
                    };
                    #endregion
                default:
                    throw new Exception($"Invalid raw account type");
            }
        }

        public async Task<IAccount> GetProfile(string address)
        {
            var account = await Get(address);

            switch (account)
            {
                case Models.Delegate delegat:
                    delegat.Contracts = await GetContracts(address, 10);
                    delegat.Delegators = await GetDelegators(address, 20);
                    delegat.Operations = await GetOperations(address, OpTypes.DefaultSet, 20);
                    break;
                case User user when user.FirstActivity != null:
                    user.Contracts = await GetContracts(address, 10);
                    user.Operations = await GetOperations(address, OpTypes.DefaultSet, 20);
                    break;
                case Contract contract:
                    contract.Contracts = await GetContracts(address, 10);
                    contract.Operations = await GetOperations(address, OpTypes.DefaultSet, 20);
                    break;
            }

            return account;
        }

        public async Task<IEnumerable<IAccount>> Get(int limit = 100, int offset = 0)
        {
            var sql = @"
                SELECT      *
                FROM        ""Accounts""
                ORDER BY    ""Id""
                OFFSET      @offset
                LIMIT       @limit";

            using var db = GetConnection();
            var rows = await db.QueryAsync(sql, new { limit, offset });

            var accounts = new List<IAccount>(rows.Count());
            foreach (var row in rows)
            {
                var metadata = Accounts.GetMetadata((int)row.Id);

                switch ((int)row.Type)
                {
                    case 0:
                        #region build user
                        var userDelegate = row.DelegateId == null ? null
                            : await Accounts.GetAsync((int)row.DelegateId);

                        var userDelegateMetadata = userDelegate == null ? null
                            : Accounts.GetMetadata(userDelegate.Id);

                        accounts.Add(new User
                        {
                            Alias = metadata?.Alias,
                            Address = row.Address,
                            Balance = row.Balance,
                            Counter = row.Balance > 0 ? row.Counter : State.GetCounter(),
                            FirstActivity = row.FirstLevel,
                            LastActivity = row.LastLevel,
                            PublicKey = row.PublicKey,
                            Delegate = userDelegate == null ? null
                            : new DelegateInfo
                            {
                                Alias = userDelegateMetadata?.Alias,
                                Address = userDelegate.Address,
                                Active = userDelegate.Staked
                            },
                            NumActivations = row.Activated == true ? 1 : 0,
                            NumContracts = row.Contracts,
                            NumDelegations = row.DelegationsCount,
                            NumOriginations = row.OriginationsCount,
                            NumReveals = row.RevealsCount,
                            NumSystem = row.SystemOpsCount,
                            NumTransactions = row.TransactionsCount
                        });
                        #endregion
                        break;
                    case 1:
                        #region build delegate
                        accounts.Add(new Models.Delegate
                        {
                            Alias = metadata?.Alias,
                            Active = row.Staked,
                            Address = row.Address,
                            PublicKey = row.PublicKey,
                            Balance = row.Balance,
                            FrozenDeposits = row.FrozenDeposits,
                            FrozenRewards = row.FrozenRewards,
                            FrozenFees = row.FrozenFees,
                            Counter = row.Counter,
                            ActivationLevel = row.ActivationLevel,
                            DeactivationLevel = row.Staked ? null : (int?)row.DeactivationLevel,
                            StakingBalance = row.StakingBalance,
                            FirstActivity = row.FirstLevel,
                            LastActivity = row.LastLevel,
                            NumActivations = row.Activated == true ? 1 : 0,
                            NumBallots = row.BallotsCount,
                            NumContracts = row.Contracts,
                            NumDelegators = row.Delegators,
                            NumDelegations = row.DelegationsCount,
                            NumDoubleBaking = row.DoubleBakingCount,
                            NumDoubleEndorsing = row.DoubleEndorsingCount,
                            NumEndorsements = row.EndorsementsCount,
                            NumNonceRevelations = row.NonceRevelationsCount,
                            NumOriginations = row.OriginationsCount,
                            NumProposals = row.ProposalsCount,
                            NumReveals = row.RevealsCount,
                            NumSystem = row.SystemOpsCount,
                            NumTransactions = row.TransactionsCount,
                        });
                        #endregion
                        break;
                    case 2:
                        #region build contract
                        var creator = row.CreatorId == null ? null
                            : await Accounts.GetAsync((int)row.CreatorId);

                        var creatorMetadata = creator == null ? null
                            : Accounts.GetMetadata(creator.Id);

                        var manager = row.ManagerId == null ? null
                            : (RawUser)await Accounts.GetAsync((int)row.ManagerId);

                        var managerMetadata = manager == null ? null
                            : Accounts.GetMetadata(manager.Id);

                        var contractDelegate = row.DelegateId == null ? null
                            : await Accounts.GetAsync((int)row.DelegateId);

                        var contractDelegateMetadata = contractDelegate == null ? null
                            : Accounts.GetMetadata(contractDelegate.Id);

                        accounts.Add(new Contract
                        {
                            Alias = metadata?.Alias,
                            Address = row.Address,
                            Kind = KindToString(row.Kind),
                            Balance = row.Balance,
                            Creator = creator == null ? null
                            : new CreatorInfo
                            {
                                Alias = creatorMetadata?.Alias,
                                Address = creator.Address
                            },
                            Manager = manager == null ? null
                            : new ManagerInfo
                            {
                                Alias = managerMetadata?.Alias,
                                Address = manager.Address,
                                PublicKey = manager.PublicKey,
                            },
                            Delegate = contractDelegate == null ? null
                            : new DelegateInfo
                            {
                                Alias = contractDelegateMetadata?.Alias,
                                Address = contractDelegate.Address,
                                Active = contractDelegate.Staked
                            },
                            FirstActivity = row.FirstLevel,
                            LastActivity = row.LastLevel,
                            NumContracts = row.Contracts,
                            NumDelegations = row.DelegationsCount,
                            NumOriginations = row.OriginationsCount,
                            NumReveals = row.RevealsCount,
                            NumSystem = row.SystemOpsCount,
                            NumTransactions = row.TransactionsCount
                        });
                        #endregion
                        break;
                }
            }

            return accounts;
        }

        public async Task<IEnumerable<RelatedContract>> GetContracts(string address, int limit = 100, int offset = 0)
        {
            var account = await Accounts.GetAsync(address);

            if (account.Contracts == 0)
                return Enumerable.Empty<RelatedContract>();

            var sql = @"
                SELECT      ""Id"", ""Kind"", ""Address"", ""Balance"", ""DelegateId""
                FROM        ""Accounts""
                WHERE       ""CreatorId"" = @accountId
                OR          ""ManagerId"" = @accountId
                ORDER BY    ""FirstLevel"" DESC
                OFFSET      @offset
                LIMIT       @limit";

            using var db = GetConnection();
            var rows = await db.QueryAsync(sql, new { accountId = account.Id, limit, offset });

            return rows.Select(row =>
            {
                var metadata = Accounts.GetMetadata((int)row.Id);

                var delegat = row.DelegatId == null ? null
                    : Accounts.Get((int)row.DelegatId);

                var delegatMetadata = delegat == null ? null
                    : Accounts.GetMetadata(delegat.Id);

                return new RelatedContract
                {
                    Kind = KindToString(row.Kind),
                    Alias = metadata?.Alias,
                    Address = row.Address,
                    Balance = row.Balance,
                    Delegate = row.DelegatId == null ? null
                         : new DelegateInfo
                         {
                             Alias = delegatMetadata?.Alias,
                             Address = delegat.Address,
                             Active = delegat.Staked
                         }
                };
            });
        }

        public async Task<IEnumerable<Delegator>> GetDelegators(string address, int limit = 100, int offset = 0)
        {
            var delegat = (RawDelegate)await Accounts.GetAsync(address);

            if (delegat.Delegators == 0)
                return Enumerable.Empty<Delegator>();

            var sql = @"
                SELECT      ""Id"", ""Address"", ""Type"", ""Balance"", ""DelegationLevel""
                FROM        ""Accounts""
                WHERE       ""DelegateId"" = @delegateId
                ORDER BY    ""DelegationLevel"" DESC
                OFFSET      @offset
                LIMIT       @limit";

            using var db = GetConnection();
            var rows = await db.QueryAsync(sql, new { delegateId = delegat.Id, limit, offset });

            return rows.Select(row =>
            {
                var metadata = Accounts.GetMetadata((int)row.Id);

                return new Delegator
                {
                    Type = TypeToString(row.Type),
                    Alias = metadata?.Alias,
                    Address = row.Address,
                    Balance = row.Balance,
                    DelegationLevel = row.DelegationLevel
                };
            });
        }

        public async Task<IEnumerable<IOperation>> GetOperations(string address, HashSet<string> types, int limit = 100)
        {
            var account = await Accounts.GetAsync(address);
            var result = new List<IOperation>(limit * 2);

            switch (account)
            {
                case RawDelegate delegat:

                    var endorsements = delegat.EndorsementsCount > 0 && types.Contains(OpTypes.Endorsement)
                        ? Operations.GetLastEndorsements(account, limit)
                        : Task.FromResult(Enumerable.Empty<EndorsementOperation>());

                    var ballots = delegat.BallotsCount > 0 && types.Contains(OpTypes.Ballot)
                        ? Operations.GetLastBallots(account, limit)
                        : Task.FromResult(Enumerable.Empty<BallotOperation>());

                    var proposals = delegat.ProposalsCount > 0 && types.Contains(OpTypes.Proposal)
                        ? Operations.GetLastProposals(account, limit)
                        : Task.FromResult(Enumerable.Empty<ProposalOperation>());

                    var activations = delegat.Activated == true && types.Contains(OpTypes.Activation)
                        ? Operations.GetLastActivations(account, limit)
                        : Task.FromResult(Enumerable.Empty<ActivationOperation>());

                    var doubleBaking = delegat.DoubleBakingCount > 0 && types.Contains(OpTypes.DoubleBaking)
                        ? Operations.GetLastDoubleBakings(account, limit)
                        : Task.FromResult(Enumerable.Empty<DoubleBakingOperation>());

                    var doubleEndorsing = delegat.DoubleEndorsingCount > 0 && types.Contains(OpTypes.DoubleEndorsing)
                        ? Operations.GetLastDoubleEndorsings(account, limit)
                        : Task.FromResult(Enumerable.Empty<DoubleEndorsingOperation>());

                    var nonceRevelations = delegat.NonceRevelationsCount > 0 && types.Contains(OpTypes.NonceRevelation)
                        ? Operations.GetLastNonceRevelations(account, limit)
                        : Task.FromResult(Enumerable.Empty<NonceRevelationOperation>());

                    var delegations = delegat.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var originations = delegat.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var transactions = delegat.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var reveals = delegat.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        endorsements,
                        proposals,
                        ballots,
                        activations,
                        doubleBaking,
                        doubleEndorsing,
                        nonceRevelations,
                        delegations,
                        originations,
                        transactions,
                        reveals);

                    result.AddRange(endorsements.Result);
                    result.AddRange(proposals.Result);
                    result.AddRange(ballots.Result);
                    result.AddRange(activations.Result);
                    result.AddRange(doubleBaking.Result);
                    result.AddRange(doubleEndorsing.Result);
                    result.AddRange(nonceRevelations.Result);
                    result.AddRange(delegations.Result);
                    result.AddRange(originations.Result);
                    result.AddRange(transactions.Result);
                    result.AddRange(reveals.Result);

                    break;
                case RawUser user:

                    var userActivations = user.Activated == true && types.Contains(OpTypes.Activation)
                        ? Operations.GetLastActivations(account, limit)
                        : Task.FromResult(Enumerable.Empty<ActivationOperation>());

                    var userDelegations = user.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var userOriginations = user.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var userTransactions = user.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var userReveals = user.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        userActivations,
                        userDelegations,
                        userOriginations,
                        userTransactions,
                        userReveals);

                    result.AddRange(userActivations.Result);
                    result.AddRange(userDelegations.Result);
                    result.AddRange(userOriginations.Result);
                    result.AddRange(userTransactions.Result);
                    result.AddRange(userReveals.Result);

                    break;
                case RawContract contract:

                    var contractDelegations = contract.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var contractOriginations = contract.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var contractTransactions = contract.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var contractReveals = contract.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        contractDelegations,
                        contractOriginations,
                        contractTransactions,
                        contractReveals);

                    result.AddRange(contractDelegations.Result);
                    result.AddRange(contractOriginations.Result);
                    result.AddRange(contractTransactions.Result);
                    result.AddRange(contractReveals.Result);

                    break;
            }

            return result.OrderByDescending(x => x.Id).Take(limit);
        }

        public async Task<IEnumerable<IOperation>> GetOperations(string address, HashSet<string> types, int fromLevel, int limit = 100)
        {
            var account = await Accounts.GetAsync(address);
            var result = new List<IOperation>(limit * 2);

            switch (account)
            {
                case RawDelegate delegat:

                    var endorsements = delegat.EndorsementsCount > 0 && types.Contains(OpTypes.Endorsement)
                        ? Operations.GetLastEndorsements(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<EndorsementOperation>());

                    var ballots = delegat.BallotsCount > 0 && types.Contains(OpTypes.Ballot)
                        ? Operations.GetLastBallots(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<BallotOperation>());

                    var proposals = delegat.ProposalsCount > 0 && types.Contains(OpTypes.Proposal)
                        ? Operations.GetLastProposals(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<ProposalOperation>());

                    var activations = delegat.Activated == true && types.Contains(OpTypes.Activation)
                        ? Operations.GetLastActivations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<ActivationOperation>());

                    var doubleBaking = delegat.DoubleBakingCount > 0 && types.Contains(OpTypes.DoubleBaking)
                        ? Operations.GetLastDoubleBakings(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<DoubleBakingOperation>());

                    var doubleEndorsing = delegat.DoubleEndorsingCount > 0 && types.Contains(OpTypes.DoubleEndorsing)
                        ? Operations.GetLastDoubleEndorsings(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<DoubleEndorsingOperation>());

                    var nonceRevelations = delegat.NonceRevelationsCount > 0 && types.Contains(OpTypes.NonceRevelation)
                        ? Operations.GetLastNonceRevelations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<NonceRevelationOperation>());

                    var delegations = delegat.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var originations = delegat.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var transactions = delegat.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var reveals = delegat.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        endorsements,
                        proposals,
                        ballots,
                        activations,
                        doubleBaking,
                        doubleEndorsing,
                        nonceRevelations,
                        delegations,
                        originations,
                        transactions,
                        reveals);

                    result.AddRange(endorsements.Result);
                    result.AddRange(proposals.Result);
                    result.AddRange(ballots.Result);
                    result.AddRange(activations.Result);
                    result.AddRange(doubleBaking.Result);
                    result.AddRange(doubleEndorsing.Result);
                    result.AddRange(nonceRevelations.Result);
                    result.AddRange(delegations.Result);
                    result.AddRange(originations.Result);
                    result.AddRange(transactions.Result);
                    result.AddRange(reveals.Result);

                    break;
                case RawUser user:

                    var userActivations = user.Activated == true && types.Contains(OpTypes.Activation)
                        ? Operations.GetLastActivations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<ActivationOperation>());

                    var userDelegations = user.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var userOriginations = user.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var userTransactions = user.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var userReveals = user.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        userActivations,
                        userDelegations,
                        userOriginations,
                        userTransactions,
                        userReveals);

                    result.AddRange(userActivations.Result);
                    result.AddRange(userDelegations.Result);
                    result.AddRange(userOriginations.Result);
                    result.AddRange(userTransactions.Result);
                    result.AddRange(userReveals.Result);

                    break;
                case RawContract contract:

                    var contractDelegations = contract.DelegationsCount > 0 && types.Contains(OpTypes.Delegation)
                        ? Operations.GetLastDelegations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<DelegationOperation>());

                    var contractOriginations = contract.OriginationsCount > 0 && types.Contains(OpTypes.Origination)
                        ? Operations.GetLastOriginations(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<OriginationOperation>());

                    var contractTransactions = contract.TransactionsCount > 0 && types.Contains(OpTypes.Transaction)
                        ? Operations.GetLastTransactions(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<TransactionOperation>());

                    var contractReveals = contract.RevealsCount > 0 && types.Contains(OpTypes.Reveal)
                        ? Operations.GetLastReveals(account, fromLevel, limit)
                        : Task.FromResult(Enumerable.Empty<RevealOperation>());

                    await Task.WhenAll(
                        contractDelegations,
                        contractOriginations,
                        contractTransactions,
                        contractReveals);

                    result.AddRange(contractDelegations.Result);
                    result.AddRange(contractOriginations.Result);
                    result.AddRange(contractTransactions.Result);
                    result.AddRange(contractReveals.Result);

                    break;
            }

            return result.OrderByDescending(x => x.Id).Take(limit);
        }

        string TypeToString(int type) => type switch
        {
            0 => "user",
            1 => "delegate",
            2 => "contract",
            _ => "unknown"
        };

        string KindToString(int kind) => kind switch
        {
            0 => "delegator_contract",
            1 => "smart_contract",
            _ => "unknown"
        };
    }
}