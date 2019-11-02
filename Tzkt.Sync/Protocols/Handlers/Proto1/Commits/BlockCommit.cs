﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Tzkt.Data.Models;

namespace Tzkt.Sync.Protocols.Proto1
{
    class BlockCommit : ProtocolCommit
    {
        public Block Block { get; protected set; }

        BlockCommit(ProtocolHandler protocol) : base(protocol) { }

        public async Task Init(RawBlock rawBlock)
        {
            var protocol = await Cache.GetProtocolAsync(rawBlock.Protocol);
            var events = BlockEvents.None;

            if (rawBlock.Level % protocol.BlocksPerCycle == 1)
                events |= BlockEvents.CycleBegin;
            else if (rawBlock.Level % protocol.BlocksPerCycle == 0)
                events |= BlockEvents.CycleEnd;

            if (protocol.Weight == 1)
                events |= BlockEvents.ProtocolBegin;
            else if (rawBlock.Metadata.Protocol != rawBlock.Metadata.NextProtocol)
                events |= BlockEvents.ProtocolEnd;

            Block = new Block
            {
                Hash = rawBlock.Hash,
                Level = rawBlock.Level,
                Protocol = protocol,
                Timestamp = rawBlock.Header.Timestamp,
                Priority = rawBlock.Header.Priority,
                Baker = (Data.Models.Delegate)await Cache.GetAccountAsync(rawBlock.Metadata.Baker),
                Events = events
            };
        }

        public async Task Init(Block block)
        {
            Block = block;
            Block.Protocol ??= await Cache.GetProtocolAsync(block.ProtoCode);
            Block.Baker ??= (Data.Models.Delegate)await Cache.GetAccountAsync(block.BakerId);
        }

        public override Task Apply()
        {
            #region entities
            var proto = Block.Protocol;
            var baker = Block.Baker;

            Db.TryAttach(proto);
            Db.TryAttach(baker);
            #endregion

            proto.Weight++;

            baker.Balance += Block.Protocol.BlockReward;
            baker.FrozenRewards += Block.Protocol.BlockReward;
            baker.FrozenDeposits += Block.Protocol.BlockDeposit;

            Db.Blocks.Add(Block);
            Cache.AddBlock(Block);

            return Task.CompletedTask;
        }

        public override Task Revert()
        {
            #region entities
            var proto = Block.Protocol;
            var baker = Block.Baker;

            Db.TryAttach(proto);
            Db.TryAttach(baker);
            #endregion

            baker.Balance -= Block.Protocol.BlockReward;
            baker.FrozenRewards -= Block.Protocol.BlockReward;
            baker.FrozenDeposits -= Block.Protocol.BlockDeposit;

            if (--proto.Weight == 0)
            {
                Db.Protocols.Remove(proto);
                Cache.RemoveProtocol(proto);
            }

            Db.Blocks.Remove(Block);

            return Task.CompletedTask;
        }

        #region static
        public static async Task<BlockCommit> Apply(ProtocolHandler proto, RawBlock rawBlock)
        {
            var commit = new BlockCommit(proto);
            await commit.Init(rawBlock);
            await commit.Apply();

            return commit;
        }

        public static async Task<BlockCommit> Revert(ProtocolHandler proto, Block block)
        {
            var commit = new BlockCommit(proto);
            await commit.Init(block);
            await commit.Revert();

            return commit;
        }
        #endregion
    }
}
