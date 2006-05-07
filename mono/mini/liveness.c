/*
 * liveness.c: liveness analysis
 *
 * Author:
 *   Dietmar Maurer (dietmar@ximian.com)
 *
 * (C) 2002 Ximian, Inc.
 */

#include "mini.h"
#include "inssel.h"
#include "aliasing.h"

#define SPILL_COST_INCREMENT (1 << (bb->nesting << 1))

//#define DEBUG_LIVENESS

#if SIZEOF_VOID_P == 8
#define BITS_PER_CHUNK 64
#else
#define BITS_PER_CHUNK 32
#endif

/* mono_bitset_mp_new:
 * 
 * allocates a MonoBitSet inside a memory pool
 */
static inline MonoBitSet* 
mono_bitset_mp_new (MonoMemPool *mp,  guint32 max_size)
{
	int size = mono_bitset_alloc_size (max_size, 0);
	gpointer mem;

	mem = mono_mempool_alloc0 (mp, size);
	return mono_bitset_mem_new (mem, max_size, MONO_BITSET_DONT_FREE);
}

static inline MonoBitSet* 
mono_bitset_mp_new_noinit (MonoMemPool *mp,  guint32 max_size)
{
	int size = mono_bitset_alloc_size (max_size, 0);
	gpointer mem;

	mem = mono_mempool_alloc (mp, size);
	return mono_bitset_mem_new (mem, max_size, MONO_BITSET_DONT_FREE);
}

G_GNUC_UNUSED static void
mono_bitset_print (MonoBitSet *set)
{
	int i;

	printf ("{");
	for (i = 0; i < mono_bitset_size (set); i++) {

		if (mono_bitset_test (set, i))
			printf ("%d, ", i);

	}
	printf ("}\n");
}

static inline void
update_live_range (MonoCompile *cfg, int idx, int block_dfn, int tree_pos)
{
	MonoLiveRange *range = &MONO_VARINFO (cfg, idx)->range;
	guint32 abs_pos = (block_dfn << 16) | tree_pos;

	if (range->first_use.abs_pos > abs_pos)
		range->first_use.abs_pos = abs_pos;

	if (range->last_use.abs_pos < abs_pos)
		range->last_use.abs_pos = abs_pos;
}

static inline void
update_live_range2 (MonoMethodVar *var, int abs_pos)
{
	if (var->range.first_use.abs_pos > abs_pos)
		var->range.first_use.abs_pos = abs_pos;

	if (var->range.last_use.abs_pos < abs_pos)
		var->range.last_use.abs_pos = abs_pos;
}

static void
update_gen_kill_set (MonoCompile *cfg, MonoBasicBlock *bb, MonoInst *inst, int inst_num)
{
	int arity;
	int max_vars = cfg->num_varinfo;

	arity = mono_burg_arity [inst->opcode];
	if (arity)
		update_gen_kill_set (cfg, bb, inst->inst_i0, inst_num);

	if (arity > 1)
		update_gen_kill_set (cfg, bb, inst->inst_i1, inst_num);

	if ((inst->ssa_op & MONO_SSA_LOAD_STORE) || (inst->opcode == OP_DUMMY_STORE)) {
		MonoLocalVariableList* affected_variables;
		MonoLocalVariableList local_affected_variable;
		
		if (cfg->aliasing_info == NULL) {
			if ((inst->ssa_op == MONO_SSA_LOAD) || (inst->ssa_op == MONO_SSA_STORE) || (inst->opcode == OP_DUMMY_STORE)) {
				local_affected_variable.variable_index = inst->inst_i0->inst_c0;
				local_affected_variable.next = NULL;
				affected_variables = &local_affected_variable;
			} else {
				affected_variables = NULL;
			}
		} else {
			affected_variables = mono_aliasing_get_affected_variables_for_inst_traversing_code (cfg->aliasing_info, inst);
		}
		
		if (inst->ssa_op & MONO_SSA_LOAD) {
			MonoLocalVariableList* affected_variable = affected_variables;
			while (affected_variable != NULL) {
				int idx = affected_variable->variable_index;
				MonoMethodVar *vi = MONO_VARINFO (cfg, idx);
				g_assert (idx < max_vars);
				if ((bb->region != -1) && !MONO_BBLOCK_IS_IN_REGION (bb, MONO_REGION_TRY)) {
					/*
					 * Variables used in exception regions can't be allocated to 
					 * registers.
					 */
					cfg->varinfo [vi->idx]->flags |= MONO_INST_VOLATILE;
				}
				update_live_range (cfg, idx, bb->dfn, inst_num); 
				if (!mono_bitset_test_fast (bb->kill_set, idx))
					mono_bitset_set_fast (bb->gen_set, idx);
				if (inst->ssa_op == MONO_SSA_LOAD)
					vi->spill_costs += SPILL_COST_INCREMENT;
				
				affected_variable = affected_variable->next;
			}
		} else if ((inst->ssa_op == MONO_SSA_STORE) || (inst->opcode == OP_DUMMY_STORE)) {
			MonoLocalVariableList* affected_variable = affected_variables;
			while (affected_variable != NULL) {
				int idx = affected_variable->variable_index;
				MonoMethodVar *vi = MONO_VARINFO (cfg, idx);
				g_assert (idx < max_vars);
				//if (arity > 0)
					//g_assert (inst->inst_i1->opcode != OP_PHI);
				if ((bb->region != -1) && !MONO_BBLOCK_IS_IN_REGION (bb, MONO_REGION_TRY)) {
					/*
					 * Variables used in exception regions can't be allocated to 
					 * registers.
					 */
					cfg->varinfo [vi->idx]->flags |= MONO_INST_VOLATILE;
				}
				update_live_range (cfg, idx, bb->dfn, inst_num); 
				mono_bitset_set_fast (bb->kill_set, idx);
				if (inst->ssa_op == MONO_SSA_STORE)
					vi->spill_costs += SPILL_COST_INCREMENT;
				
				affected_variable = affected_variable->next;
			}
		}
	} else if (inst->opcode == CEE_JMP) {
		/* Keep arguments live! */
		int i;
		for (i = 0; i < cfg->num_varinfo; i++) {
			if (cfg->varinfo [i]->opcode == OP_ARG) {
				if (!mono_bitset_test_fast (bb->kill_set, i))
					mono_bitset_set_fast (bb->gen_set, i);
			}
		}
	}
} 

static void
update_volatile (MonoCompile *cfg, MonoBasicBlock *bb, MonoInst *inst)
{
	int arity = mono_burg_arity [inst->opcode];
	int max_vars = cfg->num_varinfo;

	if (arity)
		update_volatile (cfg, bb, inst->inst_i0);

	if (arity > 1)
		update_volatile (cfg, bb, inst->inst_i1);

	if (inst->ssa_op & MONO_SSA_LOAD_STORE) {
		MonoLocalVariableList* affected_variables;
		MonoLocalVariableList local_affected_variable;
		
		if (cfg->aliasing_info == NULL) {
			if ((inst->ssa_op == MONO_SSA_LOAD) || (inst->ssa_op == MONO_SSA_STORE)) {
				local_affected_variable.variable_index = inst->inst_i0->inst_c0;
				local_affected_variable.next = NULL;
				affected_variables = &local_affected_variable;
			} else {
				affected_variables = NULL;
			}
		} else {
			affected_variables = mono_aliasing_get_affected_variables_for_inst_traversing_code (cfg->aliasing_info, inst);
		}
		
		while (affected_variables != NULL) {
			int idx = affected_variables->variable_index;
			MonoMethodVar *vi = MONO_VARINFO (cfg, idx);
			g_assert (idx < max_vars);
			cfg->varinfo [vi->idx]->flags |= MONO_INST_VOLATILE;
			
			affected_variables = affected_variables->next;
		}
	}
} 

static void
visit_bb (MonoCompile *cfg, MonoBasicBlock *bb, GSList **visited)
{
	int i;
	MonoInst *ins;

	if (g_slist_find (*visited, bb))
		return;

	if (cfg->new_ir) {
		/* FIXME: Rewrite this using the aliasing framework */
		for (ins = bb->code; ins; ins = ins->next) {
			const char *spec = INS_INFO (ins->opcode);
			int regtype, srcindex, sreg;

			if (ins->opcode == OP_NOP)
				continue;

			/* DREG */
			regtype = spec [MONO_INST_DEST];
			g_assert (((ins->dreg == -1) && (regtype == ' ')) || ((ins->dreg != -1) && (regtype != ' ')));
				
			if ((ins->dreg != -1) && get_vreg_to_inst (cfg, ins->dreg)) {
				MonoInst *var = get_vreg_to_inst (cfg, ins->dreg);
				int idx = var->inst_c0;
				MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

				cfg->varinfo [vi->idx]->flags |= MONO_INST_VOLATILE;
			}
			
			/* SREGS */
			for (srcindex = 0; srcindex < 2; ++srcindex) {
				regtype = spec [(srcindex == 0) ? MONO_INST_SRC1 : MONO_INST_SRC2];
				sreg = srcindex == 0 ? ins->sreg1 : ins->sreg2;

				g_assert (((sreg == -1) && (regtype == ' ')) || ((sreg != -1) && (regtype != ' ')));
				if ((sreg != -1) && get_vreg_to_inst (cfg, sreg)) {
					MonoInst *var = get_vreg_to_inst (cfg, sreg);
					int idx = var->inst_c0;
					MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

					cfg->varinfo [vi->idx]->flags |= MONO_INST_VOLATILE;
				}
			}
		}
	} else {
		if (cfg->aliasing_info != NULL)
			mono_aliasing_initialize_code_traversal (cfg->aliasing_info, bb);

		for (ins = bb->code; ins; ins = ins->next) {
			update_volatile (cfg, bb, ins);
		}
	}

	*visited = g_slist_append (*visited, bb);

	/* 
	 * Need to visit all bblocks reachable from this one since they can be
	 * reached during exception handling.
	 */
	for (i = 0; i < bb->out_count; ++i) {
		visit_bb (cfg, bb->out_bb [i], visited);
	}
}

static void
handle_exception_clauses (MonoCompile *cfg)
{
	MonoBasicBlock *bb;
	GSList *visited = NULL;

	/*
	 * Variables in exception handler register cannot be allocated to registers
	 * so make them volatile. See bug #42136. This will not be neccessary when
	 * the back ends could guarantee that the variables will be in the
	 * correct registers when a handler is called.
	 * This includes try blocks too, since a variable in a try block might be
	 * accessed after an exception handler has been run.
	 */
	for (bb = cfg->bb_entry; bb; bb = bb->next_bb) {

		if (bb->region == -1 || MONO_BBLOCK_IS_IN_REGION (bb, MONO_REGION_TRY))
			continue;

		visit_bb (cfg, bb, &visited);
	}
	g_slist_free (visited);
}

static void
analyze_liveness_bb (MonoCompile *cfg, MonoBasicBlock *bb)
{
	MonoInst *ins;
	int sreg, inst_num;
	MonoMethodVar *vars = cfg->vars;
	guint32 abs_pos = (bb->dfn << 16);
	
	for (inst_num = 0, ins = bb->code; ins; ins = ins->next, inst_num += 2) {
		const char *spec = INS_INFO (ins->opcode);

#ifdef DEBUG_LIVENESS
			printf ("\t"); mono_print_ins (ins);
#endif

		if (ins->opcode == OP_NOP)
			continue;

		if (ins->opcode == OP_LDADDR) {
			MonoInst *var = ins->inst_p0;
			int idx = var->inst_c0;
			MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

#ifdef DEBUG_LIVENESS
			printf ("\tGEN: R%d(%d)\n", var->dreg, idx);
#endif
			update_live_range2 (&vars [idx], abs_pos + inst_num); 
			if (!mono_bitset_test_fast (bb->kill_set, idx))
				mono_bitset_set_fast (bb->gen_set, idx);
			vi->spill_costs += SPILL_COST_INCREMENT;
		}				

		/* SREGs must come first, so MOVE r <- r is handled correctly */

		/* SREG1 */
		sreg = ins->sreg1;
		if ((spec [MONO_INST_SRC1] != ' ') && (sreg != -1) && get_vreg_to_inst (cfg, sreg)) {
			MonoInst *var = get_vreg_to_inst (cfg, sreg);
			int idx = var->inst_c0;
			MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

#ifdef DEBUG_LIVENESS
			printf ("\tGEN: R%d(%d)\n", sreg, idx);
#endif
			update_live_range2 (&vars [idx], abs_pos + inst_num); 
			if (!mono_bitset_test_fast (bb->kill_set, idx))
				mono_bitset_set_fast (bb->gen_set, idx);
			vi->spill_costs += SPILL_COST_INCREMENT;
		}

		/* SREG2 */
		sreg = ins->sreg2;
		if ((spec [MONO_INST_SRC2] != ' ') && (sreg != -1) && get_vreg_to_inst (cfg, sreg)) {
			MonoInst *var = get_vreg_to_inst (cfg, sreg);
			int idx = var->inst_c0;
			MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

#ifdef DEBUG_LIVENESS
			printf ("\tGEN: R%d(%d)\n", sreg, idx);
#endif
			update_live_range2 (&vars [idx], abs_pos + inst_num); 
			if (!mono_bitset_test_fast (bb->kill_set, idx))
				mono_bitset_set_fast (bb->gen_set, idx);
			vi->spill_costs += SPILL_COST_INCREMENT;
		}

		/* DREG */
		if ((spec [MONO_INST_DEST] != ' ') && (ins->dreg != -1) && get_vreg_to_inst (cfg, ins->dreg)) {
			MonoInst *var = get_vreg_to_inst (cfg, ins->dreg);
			int idx = var->inst_c0;
			MonoMethodVar *vi = MONO_VARINFO (cfg, idx);

			if (MONO_IS_STORE_MEMBASE (ins)) {
				update_live_range2 (&vars [idx], abs_pos + inst_num); 
				if (!mono_bitset_test_fast (bb->kill_set, idx))
					mono_bitset_set_fast (bb->gen_set, idx);
				vi->spill_costs += SPILL_COST_INCREMENT;
			} else {
#ifdef DEBUG_LIVENESS
				printf ("\tKILL: R%d(%d)\n", ins->dreg, idx);
#endif
				update_live_range2 (&vars [idx], abs_pos + inst_num + 1); 
				mono_bitset_set_fast (bb->kill_set, idx);
				vi->spill_costs += SPILL_COST_INCREMENT;
			}
		}
	}
}

static void
optimize_initlocals (MonoCompile *cfg);

/* generic liveness analysis code. CFG specific parts are 
 * in update_gen_kill_set()
 */
void
mono_analyze_liveness (MonoCompile *cfg)
{
	MonoBitSet *old_live_out_set;
	int i, j, max_vars = cfg->num_varinfo;
	int out_iter;
	gboolean *in_worklist;
	MonoBasicBlock **worklist;
	guint32 l_end;
	int bitsize;
	guint8 *mem;

#ifdef DEBUG_LIVENESS
	printf ("LIVENESS %s\n", mono_method_full_name (cfg->method, TRUE));
#endif

	g_assert (!(cfg->comp_done & MONO_COMP_LIVENESS));

	cfg->comp_done |= MONO_COMP_LIVENESS;
	
	if (max_vars == 0)
		return;

	bitsize = mono_bitset_alloc_size (max_vars, 0);
	mem = mono_mempool_alloc0 (cfg->mempool, cfg->num_bblocks * bitsize * 4);

	for (i = 0; i < cfg->num_bblocks; ++i) {
		MonoBasicBlock *bb = cfg->bblocks [i];

		bb->gen_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
		mem += bitsize;
		bb->kill_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
		mem += bitsize;
		/* Initialized later */
		bb->live_in_set = NULL;
		bb->live_out_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
		mem += bitsize;
	}
	for (i = 0; i < max_vars; i ++) {
		MONO_VARINFO (cfg, i)->range.first_use.abs_pos = ~ 0;
		MONO_VARINFO (cfg, i)->range.last_use .abs_pos =   0;
		MONO_VARINFO (cfg, i)->spill_costs = 0;
	}

	for (i = 0; i < cfg->num_bblocks; ++i) {
		MonoBasicBlock *bb = cfg->bblocks [i];
		MonoInst *inst;
		int tree_num;

		if (cfg->aliasing_info != NULL)
			mono_aliasing_initialize_code_traversal (cfg->aliasing_info, bb);

		if (cfg->new_ir) {
			analyze_liveness_bb (cfg, bb);
		} else {
			for (tree_num = 0, inst = bb->code; inst; inst = inst->next, tree_num++) {
#ifdef DEBUG_LIVENESS
				mono_print_tree (inst); printf ("\n");
#endif
				update_gen_kill_set (cfg, bb, inst, tree_num);
			}
		}

#ifdef DEBUG_LIVENESS
		printf ("BLOCK BB%d (", bb->block_num);
		for (j = 0; j < bb->out_count; j++) 
			printf ("BB%d, ", bb->out_bb [j]->block_num);
		
		printf (")\n");
		printf ("GEN  BB%d: ", bb->block_num); mono_bitset_print (bb->gen_set);
		printf ("KILL BB%d: ", bb->block_num); mono_bitset_print (bb->kill_set);
#endif
	}

	old_live_out_set = mono_bitset_new (max_vars, 0);
	in_worklist = g_new0 (gboolean, cfg->num_bblocks + 1);

	worklist = g_new (MonoBasicBlock *, cfg->num_bblocks + 1);
	l_end = 0;

	/*
	 * This is a backward dataflow analysis problem, so we process blocks in
	 * decreasing dfn order, this speeds up the iteration.
	 */
	for (i = 0; i < cfg->num_bblocks; i ++) {
		MonoBasicBlock *bb = cfg->bblocks [i];

		worklist [l_end ++] = bb;
		in_worklist [bb->dfn] = TRUE;
	}

	out_iter = 0;

	while (l_end != 0) {
		MonoBasicBlock *bb = worklist [--l_end];
		MonoBasicBlock *out_bb;
		gboolean changed;

		in_worklist [bb->dfn] = FALSE;

#ifdef DEBUG_LIVENESS
		printf ("P: %d(%d): IN: ", bb->block_num, bb->dfn);
		for (j = 0; j < bb->in_count; ++j) 
			printf ("BB%d ", bb->in_bb [j]->block_num);
		printf ("OUT:");
		for (j = 0; j < bb->out_count; ++j) 
			printf ("BB%d ", bb->out_bb [j]->block_num);
		printf ("\n");
#endif


		if (bb->out_count == 0)
			continue;

		out_iter ++;

		if (!bb->live_in_set) {
			/* First pass over this bblock */
			changed = TRUE;
		}
		else {
			changed = FALSE;
			mono_bitset_copyto_fast (bb->live_out_set, old_live_out_set);
		}
 
		for (j = 0; j < bb->out_count; j++) {
			out_bb = bb->out_bb [j];

			if (!out_bb->live_in_set) {
				out_bb->live_in_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
				mem += bitsize;

				mono_bitset_copyto_fast (out_bb->live_out_set, out_bb->live_in_set);
				mono_bitset_sub_fast (out_bb->live_in_set, out_bb->kill_set);
				mono_bitset_union_fast (out_bb->live_in_set, out_bb->gen_set);
			}

			mono_bitset_union_fast (bb->live_out_set, out_bb->live_in_set);
		}
				
		if (changed || !mono_bitset_equal (old_live_out_set, bb->live_out_set)) {
			if (!bb->live_in_set) {
				bb->live_in_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
				mem += bitsize;
			}
			mono_bitset_copyto_fast (bb->live_out_set, bb->live_in_set);
			mono_bitset_sub_fast (bb->live_in_set, bb->kill_set);
			mono_bitset_union_fast (bb->live_in_set, bb->gen_set);

			for (j = 0; j < bb->in_count; j++) {
				MonoBasicBlock *in_bb = bb->in_bb [j];
				/* 
				 * Some basic blocks do not seem to be in the 
				 * cfg->bblocks array...
				 */
				if (in_bb->gen_set && !in_worklist [in_bb->dfn]) {
#ifdef DEBUG_LIVENESS
					printf ("\tADD: %d\n", in_bb->block_num);
#endif
					/*
					 * Put the block at the top of the stack, so it
					 * will be processed right away.
					 */
					worklist [l_end ++] = in_bb;
					in_worklist [in_bb->dfn] = TRUE;
				}
			}
		}
	}

#ifdef DEBUG_LIVENESS
		printf ("IT: %d %d.\n", cfg->num_bblocks, out_iter);
#endif

	mono_bitset_free (old_live_out_set);

	g_free (worklist);
	g_free (in_worklist);

	/* Compute live_in_set for bblocks skipped earlier */
	for (i = 0; i < cfg->num_bblocks; ++i) {
		MonoBasicBlock *bb = cfg->bblocks [i];

		if (!bb->live_in_set) {
			bb->live_in_set = mono_bitset_mem_new (mem, max_vars, MONO_BITSET_DONT_FREE);
			mem += bitsize;

			mono_bitset_copyto_fast (bb->live_out_set, bb->live_in_set);
			mono_bitset_sub_fast (bb->live_in_set, bb->kill_set);
			mono_bitset_union_fast (bb->live_in_set, bb->gen_set);
		}
	}

	for (i = 0; i < cfg->num_bblocks; ++i) {
		MonoBasicBlock *bb = cfg->bblocks [i];
		guint32 rem, max;
		guint32 abs_pos = (bb->dfn << 16);
		MonoMethodVar *vars = cfg->vars;

		if (!bb->live_out_set)
			continue;

		rem = max_vars % BITS_PER_CHUNK;
		max = ((max_vars + (BITS_PER_CHUNK -1)) / BITS_PER_CHUNK);
		for (j = 0; j < max; ++j) {
			gsize bits_in;
			gsize bits_out;
			int k, end;

			bits_in = mono_bitset_get_fast (bb->live_in_set, j);
			bits_out = mono_bitset_get_fast (bb->live_out_set, j);

			if (j == max)
				end = (j * BITS_PER_CHUNK) + rem;
			else
				end = (j * BITS_PER_CHUNK) + BITS_PER_CHUNK;

			k = (j * BITS_PER_CHUNK);
			while ((bits_in || bits_out)) {
				if (bits_in & 1)
					update_live_range2 (&vars [k], abs_pos + 0);
				if (bits_out & 1)
					update_live_range2 (&vars [k], abs_pos + 0xffff);
				bits_in >>= 1;
				bits_out >>= 1;
				k ++;
			}
		}
	}

	/* todo: remove code when we have verified that the liveness for try/catch blocks
	 * works perfectly 
	 */
	/* 
	 * Currently, this can't be commented out since exception blocks are not
	 * processed during liveness analysis.
	 */
	handle_exception_clauses (cfg);

	/*
	 * Arguments need to have their live ranges extended to the beginning of
	 * the method to account for the arg reg/memory -> global register copies
	 * in the prolog (bug #74992).
	 */

	for (i = 0; i < max_vars; i ++) {
		MonoMethodVar *vi = MONO_VARINFO (cfg, i);
		if (cfg->varinfo [vi->idx]->opcode == OP_ARG)
			vi->range.first_use.abs_pos = 0;
	}

#ifdef DEBUG_LIVENESS
	for (i = cfg->num_bblocks - 1; i >= 0; i--) {
		MonoBasicBlock *bb = cfg->bblocks [i];
		
		printf ("LIVE IN  BB%d: ", bb->block_num); 
		mono_bitset_print (bb->live_in_set); 
		printf ("LIVE OUT BB%d: ", bb->block_num); 
		mono_bitset_print (bb->live_out_set); 
	}
#endif

	optimize_initlocals (cfg);
}

/**
 * optimize_initlocals:
 *
 * Try to optimize away some of the redundant initialization code inserted because of
 * 'locals init' using the liveness information.
 */
static void
optimize_initlocals (MonoCompile *cfg)
{
	MonoBitSet *used;
	MonoInst *ins;
	MonoBasicBlock *initlocals_bb;

	used = mono_bitset_new (cfg->next_vireg + 1, 0);

	mono_bitset_clear_all (used);
	initlocals_bb = cfg->bb_entry->next_bb;
	for (ins = initlocals_bb->code; ins; ins = ins->next) {
		const char *spec = INS_INFO (ins->opcode);

		if (spec [MONO_INST_SRC1] != ' ')
			mono_bitset_set_fast (used, ins->sreg1);
		if (spec [MONO_INST_SRC2] != ' ')
			mono_bitset_set_fast (used, ins->sreg2);
		if (MONO_IS_STORE_MEMBASE (ins))
			mono_bitset_set_fast (used, ins->dreg);
	}

	for (ins = initlocals_bb->code; ins; ins = ins->next) {
		const char *spec = INS_INFO (ins->opcode);

		/* Look for statements whose dest is not used in this bblock and not live on exit. */
		if ((spec [MONO_INST_DEST] != ' ') && !MONO_IS_STORE_MEMBASE (ins)) {
			MonoInst *var = get_vreg_to_inst (cfg, ins->dreg);

			if (var && !mono_bitset_test_fast (used, ins->dreg) && !mono_bitset_test_fast (initlocals_bb->live_out_set, var->inst_c0) && (var != cfg->ret) && !(var->flags & (MONO_INST_VOLATILE|MONO_INST_INDIRECT))) {
				//printf ("DEAD: "); mono_print_ins (ins);
				if ((ins->opcode == OP_ICONST) || (ins->opcode == OP_I8CONST) || (ins->opcode == OP_R8CONST)) {
					NULLIFY_INS (ins);
					MONO_VARINFO (cfg, var->inst_c0)->spill_costs -= 1;
					/* 
					 * We should shorten the liveness interval of these vars as well, but
					 * don't have enough info to do that.
					 */
				}
			}
		}
	}

	g_free (used);
}
