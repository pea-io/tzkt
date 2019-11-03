﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tzkt.Data.Models;

namespace Tzkt.Sync.Protocols.Initiator
{
    class BlockCommit : ProtocolCommit
    {
        public Block Block { get; private set; }

        public BlockCommit(ProtocolHandler protocol) : base(protocol) { }

        public async Task Init(RawBlock rawBlock)
        {
            Block = new Block
            {
                Hash = rawBlock.Hash,
                Level = rawBlock.Level,
                Protocol = await Cache.GetProtocolAsync(rawBlock.Protocol),
                Timestamp = rawBlock.Header.Timestamp,
                Events = BlockEvents.CycleBegin
                    | BlockEvents.ProtocolBegin
                    | BlockEvents.ProtocolEnd
                    | BlockEvents.VotingPeriodBegin
            };
        }

        public async Task Init(Block block)
        {
            Block = block;
            Block.Protocol ??= await Cache.GetProtocolAsync(block.ProtoCode);
        }

        public override Task Apply()
        {
            Db.TryAttach(Block.Protocol);

            Block.Protocol.Weight++;

            Db.Blocks.Add(Block);
            Cache.AddBlock(Block);

            return Task.CompletedTask;
        }

        public override Task Revert()
        {
            Db.Protocols.Remove(Block.Protocol);
            Cache.RemoveProtocol(Block.Protocol);

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