﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Nethermind.Api;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Processing;
using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Evm.Tracing;
using Nethermind.Int256;
using Nethermind.JsonRpc;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.State;

namespace Nethermind.Merge.Plugin.Handlers
{
    public class ExecutePayloadHandler: IHandler<BlockRequestResult, ExecutePayloadResult>
    {
        private const string AndWontBeAcceptedToTheTree = "and wont be accepted to the tree";
        private readonly IBlockTree _blockTree;
        private readonly IBlockPreprocessorStep _preprocessor;
        private readonly IBlockchainProcessor _processor;
        private readonly PayloadManager _payloadManager;
        private readonly IStateProvider _stateProvider;
        private readonly IInitConfig _initConfig;
        private readonly ILogger _logger;
        private readonly LruCache<Keccak, bool> _latestBlocks = new(50, "LatestBlocks");
        
        public ExecutePayloadHandler(
            IBlockTree blockTree,
            IBlockPreprocessorStep preprocessor,
            IBlockchainProcessor processor,
            PayloadManager payloadManager,
            IStateProvider stateProvider,
            IInitConfig initConfig,
            ILogManager logManager)
        {
            _blockTree = blockTree;
            _preprocessor = preprocessor;
            _processor = processor;
            _payloadManager = payloadManager;
            _stateProvider = stateProvider;
            _initConfig = initConfig;
            _logger = logManager.GetClassLogger();
        }

        public ResultWrapper<ExecutePayloadResult> Handle(BlockRequestResult request)
        {
            _payloadManager.TryAddPayloadBlockHash(request.BlockHash);
            ExecutePayloadResult executePayloadResult = new() {BlockHash = request.BlockHash};

            VerificationStatus status = ValidateRequestAndProcess(request, out Block? processedBlock);
            if ((status & VerificationStatus.Known) != 0)
            {
                executePayloadResult.Status = VerificationStatus.Known;
                return ResultWrapper<ExecutePayloadResult>.Success(executePayloadResult);
            }
            else if (status == VerificationStatus.Invalid || processedBlock is null)
            {
                _payloadManager.MarkPayloadValidationAsFinished(request.BlockHash);
                executePayloadResult.Status = VerificationStatus.Invalid;
                return ResultWrapper<ExecutePayloadResult>.Success(executePayloadResult);
            }
            
            executePayloadResult.Status = VerificationStatus.Valid;
            
            if (!_payloadManager.CheckConsensusValidatedResult(request.BlockHash, out bool isValid))
            {
                _payloadManager.MarkPayloadValidationAsFinished(request.BlockHash);
                _payloadManager.TryAddValidPayload(request.BlockHash, processedBlock);
            }
            else if (isValid)
            {
                _payloadManager.ProcessValidatedPayload(processedBlock);
            }

            return ResultWrapper<ExecutePayloadResult>.Success(executePayloadResult);
        }

        private VerificationStatus ValidateRequestAndProcess(BlockRequestResult request, out Block? processedBlock)
        {
            processedBlock = null;
            
            if (request.TryGetBlock(out Block? block) && block != null)
            {
                bool hashValid = CheckInputIs(request, request.BlockHash, block.CalculateHash(), nameof(request.BlockHash));

                if (hashValid)
                {
                    bool isRecentBlock = _latestBlocks.TryGet(request.BlockHash, out bool isValid);
                    if (isRecentBlock)
                    {
                        return VerificationStatus.Known | (isValid ? VerificationStatus.Valid : VerificationStatus.Invalid);
                    }
                    else
                    {
                        processedBlock = _blockTree.FindBlock(request.BlockHash, BlockTreeLookupOptions.None);

                        if (processedBlock != null)
                        {
                            return VerificationStatus.Valid | VerificationStatus.Known;
                        }

                        bool validAndProcessed =
                            CheckInput(request)
                            && CheckParent(request.ParentHash, out BlockHeader? parent)
                            && Process(block, parent!, out processedBlock);

                        _latestBlocks.Set(request.BlockHash, validAndProcessed);

                        return validAndProcessed ? VerificationStatus.Valid : VerificationStatus.Invalid;
                    }
                }
            }
            else
            {
                if (_logger.IsWarn) _logger.Warn($"Block {request} could not be parsed as block {AndWontBeAcceptedToTheTree}.");
            }

            return VerificationStatus.Invalid;
        }

        private bool CheckParent(Keccak? parentHash, out BlockHeader? parent)
        {
            if (parentHash == null)
            {
                parent = null;
                return false;
            }
            else
            {
                parent = _blockTree.FindHeader(parentHash);
                return parent != null;
            }
        }

        private bool Process(Block block, BlockHeader parent, out Block? processedBlock)
        {
            block.Header.TotalDifficulty = parent.TotalDifficulty + block.Difficulty;
            
            Keccak currentStateRoot = _stateProvider.ResetStateTo(parent.StateRoot!);
            try
            {
                _preprocessor.RecoverData(block);
                processedBlock = _processor.Process(block, GetProcessingOptions(), NullBlockTracer.Instance);
                if (processedBlock == null)
                {
                    if (_logger.IsWarn)
                    {
                        _logger.Warn($"Block {block.ToString(Block.Format.FullHashAndNumber)} cannot be processed {AndWontBeAcceptedToTheTree}.");
                    }

                    return false;
                }
            }
            finally
            {
                _stateProvider.ResetStateTo(currentStateRoot);
            }

            return true;
        }

        private ProcessingOptions GetProcessingOptions()
        {
            ProcessingOptions options = ProcessingOptions.EthereumMerge;
            if (_initConfig.StoreReceipts)
            {
                options |= ProcessingOptions.StoreReceipts;
            }

            return options;
        }

        private bool CheckInput(BlockRequestResult request)
        {
            bool validDifficulty = CheckInputIs(request, request.Difficulty, UInt256.Zero, nameof(request.Difficulty));
            bool validNonce = CheckInputIs(request, request.Nonce, 0ul, nameof(request.Nonce));
            bool validMixHash = CheckInputIs(request, request.MixHash, Keccak.Zero, nameof(request.MixHash));
            bool validUncles = CheckInputIs(request, request.Uncles, Array.Empty<Keccak>(), nameof(request.Uncles));
            
            return validDifficulty
                   && validNonce
                   && validMixHash
                   && validUncles;
        }

        private bool CheckInputIs<T>(BlockRequestResult request, T value, T expected, string name)
        {
            if (!Equals(value, expected))
            {
                if (_logger.IsWarn) _logger.Warn($"Block {request} has invalid {name}, expected {expected}, got {value} {AndWontBeAcceptedToTheTree}.");
                return false;
            }

            return true;
        }
        
        private bool CheckInputIs<T>(BlockRequestResult request, IEnumerable<T>? value, IEnumerable<T> expected, string name)
        {
            if (!(value ?? Array.Empty<T>()).SequenceEqual(expected))
            {
                if (_logger.IsWarn) _logger.Warn($"Block {request} has invalid {name}, expected {expected}, got {value} {AndWontBeAcceptedToTheTree}.");
                return false;
            }

            return true;
        }
    }
}
