﻿using NBitcoin;
using NTumbleBit.PuzzlePromise;
using NTumbleBit.PuzzleSolver;
using System;
using System.Linq;

namespace NTumbleBit.ClassicTumbler
{
	public enum TumblerClientSessionStates
	{
		WaitingVoucher,
		WaitingTumblerClientTransactionKey,
		WaitingClientTransaction,
		WaitingSolvedVoucher,
		WaitingGenerateTumblerTransactionKey,
		WaitingTumblerEscrow,
		PromisePhase,
	}
	public class ClientChannelNegotiation
	{
		public class State
		{
			public State()
			{
			}

			public TumblerClientSessionStates Status
			{
				get; set;
			}

			public int CycleStart
			{
				get; set;
			}

			public byte[] SignedVoucher
			{
				get; set;
			}
			public UnsignedVoucherInformation UnsignedVoucher
			{
				get; set;
			}
			public PuzzleValue BlindedVoucher
			{
				get; set;
			}
			public BlindFactor BlindedVoucherFactor
			{
				get; set;
			}

			public Script TumblerEscrow
			{
				get; set;
			}
			public Script ClientEscrow
			{
				get; set;
			}
			public Key TumblerEscrowKey
			{
				get;
				set;
			}
			public Key ClientEscrowKey
			{
				get;
				set;
			}
			public int TumblerEscrowKeyReference
			{
				get;
				set;
			}
		}
		public ClientChannelNegotiation(ClassicTumblerParameters parameters, int cycleStart)
		{
			InternalState = new State();
			Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
			InternalState.CycleStart = cycleStart;
		}

		public ClientChannelNegotiation(ClassicTumblerParameters parameters, State state)
		{
			if (state == null)
				throw new ArgumentNullException(nameof(state));
			Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
			InternalState = Serializer.Clone(state);
		}

		private State InternalState
		{
			get; set;
		}


		public TumblerClientSessionStates Status => InternalState.Status;

		public State GetInternalState()
		{
			var clone = Serializer.Clone(InternalState);
			return clone;
		}


		public ClassicTumblerParameters Parameters
		{
			get;
			private set;
		}


		public void ReceiveUnsignedVoucher(UnsignedVoucherInformation voucher)
		{
			AssertState(TumblerClientSessionStates.WaitingVoucher);
			var puzzle = new Puzzle(Parameters.VoucherKey, voucher.Puzzle);
			InternalState.UnsignedVoucher = voucher;
			BlindFactor factor = null;
			InternalState.BlindedVoucher = puzzle.Blind(ref factor).PuzzleValue;
			InternalState.BlindedVoucherFactor = factor;
			InternalState.Status = TumblerClientSessionStates.WaitingTumblerClientTransactionKey;
		}

		/// <summary>
		/// Receiving the Tumbler escrow key of Client Escrow.
		/// </summary>
		/// <param name="tumblerKey"></param>
		/// <param name="keyReference"></param>
		public void ReceiveTumblerEscrowKey(PubKey tumblerKey, int keyReference)
		{
			AssertState(TumblerClientSessionStates.WaitingTumblerClientTransactionKey);
			var escrow = new Key();

			InternalState.ClientEscrow = new EscrowScriptPubKeyParameters
			{
				Initiator = escrow.PubKey,
				Receiver = tumblerKey,
				LockTime = GetCycle().GetClientLockTime()
			}.ToScript();
			InternalState.ClientEscrowKey = escrow;
			InternalState.TumblerEscrowKeyReference = keyReference;
			InternalState.Status = TumblerClientSessionStates.WaitingClientTransaction;
		}

		public TxOut BuildClientEscrowTxOut()
		{
			AssertState(TumblerClientSessionStates.WaitingClientTransaction);
			return new TxOut(Parameters.Denomination + Parameters.Fee, InternalState.ClientEscrow.Hash);
		}

		public SolverClientSession SetClientSignedTransaction(Transaction transaction, Script redeemDestination)
		{
			AssertState(TumblerClientSessionStates.WaitingClientTransaction);
			var expectedTxout = BuildClientEscrowTxOut();
			var output = transaction.Outputs.AsIndexedOutputs().Single(o => o.TxOut.ScriptPubKey == expectedTxout.ScriptPubKey && o.TxOut.Value == expectedTxout.Value);
			var solver = new SolverClientSession(Parameters.CreateSolverParamaters());
			solver.ConfigureEscrowedCoin(new Coin(output).ToScriptCoin(InternalState.ClientEscrow), InternalState.ClientEscrowKey, redeemDestination);
			InternalState.Status = TumblerClientSessionStates.WaitingSolvedVoucher;
			return solver;
		}

		public void CheckVoucherSolution(PuzzleSolution blindedVoucherSignature)
		{
			AssertState(TumblerClientSessionStates.WaitingSolvedVoucher);
			var solution = blindedVoucherSignature.Unblind(Parameters.VoucherKey, InternalState.BlindedVoucherFactor);
			if(!InternalState.UnsignedVoucher.Puzzle.WithRsaKey(Parameters.VoucherKey).Verify(solution))
				throw new PuzzleException("Incorrect puzzle solution");
			InternalState.BlindedVoucherFactor = null;
			InternalState.SignedVoucher = new XORKey(solution).XOR(InternalState.UnsignedVoucher.EncryptedSignature);
			InternalState.UnsignedVoucher.EncryptedSignature = null;
			InternalState.ClientEscrowKey = null;
			InternalState.Status = TumblerClientSessionStates.WaitingGenerateTumblerTransactionKey;
		}

		public OpenChannelRequest GetOpenChannelRequest()
		{
			AssertState(TumblerClientSessionStates.WaitingGenerateTumblerTransactionKey);
			var escrow = new Key();
			InternalState.TumblerEscrowKey = escrow;
			InternalState.Status = TumblerClientSessionStates.WaitingTumblerEscrow;
			var result = new OpenChannelRequest
			{
				EscrowKey = escrow.PubKey,
				Signature = InternalState.SignedVoucher,
				CycleStart = InternalState.UnsignedVoucher.CycleStart,
				Nonce = InternalState.UnsignedVoucher.Nonce,
			};
			InternalState.SignedVoucher = null;
			InternalState.UnsignedVoucher = null;
			return result;
		}

		public PromiseClientSession ReceiveTumblerEscrowedCoin(ScriptCoin escrowedCoin)
		{
			AssertState(TumblerClientSessionStates.WaitingTumblerEscrow);
			var escrow = EscrowScriptPubKeyParameters.GetFromCoin(escrowedCoin);
			var expectedEscrow = new EscrowScriptPubKeyParameters
			{
				Initiator = escrow?.Initiator,
				Receiver = InternalState.TumblerEscrowKey.PubKey,
				LockTime = GetCycle().GetTumblerLockTime()
			};
			if(escrow == null || escrow != expectedEscrow)
				throw new PuzzleException("invalid-escrow");
			if(escrowedCoin.Amount != Parameters.Denomination)
				throw new PuzzleException("invalid-amount");


			InternalState.Status = TumblerClientSessionStates.PromisePhase;
			var session = new PromiseClientSession(Parameters.CreatePromiseParamaters());
			session.ConfigureEscrowedCoin(escrowedCoin, InternalState.TumblerEscrowKey);
			InternalState.TumblerEscrowKey = null;
			return session;
		}

		public CycleParameters GetCycle() => Parameters.CycleGenerator.GetCycle(InternalState.CycleStart);

		private void AssertState(TumblerClientSessionStates state)
		{
			if(state != InternalState.Status)
				throw new InvalidOperationException("Invalid state, actual " + InternalState.Status + " while expected is " + state);
		}
	}
}