//  Copyright (c) 2021 Demerzel Solutions Limited
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

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.TxPool
{
    public class NullTxPool : ITxPool
    {
        private NullTxPool() { }

        public static NullTxPool Instance { get; } = new();

        public int GetPendingTransactionsCount() => 0;

        public WrappedTransaction[] GetPendingTransactions() => Array.Empty<WrappedTransaction>();
        
        public WrappedTransaction[] GetOwnPendingTransactions() => Array.Empty<WrappedTransaction>();
        
        public IDictionary<Address, WrappedTransaction[]> GetPendingTransactionsBySender() => new Dictionary<Address, WrappedTransaction[]>();

        public void AddPeer(ITxPoolPeer peer) { }

        public void RemovePeer(PublicKey nodeId) { }
        
        public AddTxResult AddTransaction(Transaction tx, TxHandlingOptions txHandlingOptions) => AddTxResult.Added;

        public bool RemoveTransaction(Transaction tx, bool removeBelowThisTxNonce) => false;
        
        public void RemoveOrUpdateBuckets() { }
        
        public bool IsInHashCache(Keccak hash) => false;

        public bool TryGetPendingTransaction(Keccak hash, out WrappedTransaction? wTx)
        {
            wTx = null;
            return false;
        }

        public UInt256 ReserveOwnTransactionNonce(Address address) => UInt256.Zero;

        public event EventHandler<TxEventArgs> NewDiscovered
        {
            add { }
            remove { }
        }

        public event EventHandler<TxEventArgs> NewPending
        {
            add { }
            remove { }
        }

        public event EventHandler<TxEventArgs> RemovedPending
        {
            add { }
            remove { }
        }

        public uint FutureNonceRetention { get; } = 16;
        public long? BlockGasLimit { get; set; }
        public UInt256 CurrentBaseFee { get; set; }
    }
}
