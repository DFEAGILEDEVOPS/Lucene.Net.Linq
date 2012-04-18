﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Lucene.Net.Documents;
using Lucene.Net.Linq.Transformation;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Remotion.Linq;
using Remotion.Linq.Clauses;
using Remotion.Linq.Clauses.ExpressionTreeVisitors;

namespace Lucene.Net.Linq
{
    internal class DocumentQueryExecutor : LuceneQueryExecutor<Document>
    {
        public DocumentQueryExecutor(Directory directory, Context context) : base(directory, context)
        {
        }

        protected override void SetCurrentDocument(Document doc)
        {
            CurrentDocument = doc;
        }
    }

    internal class DocumentHolderQueryExecutor<TDocument> : LuceneQueryExecutor<TDocument> where TDocument : IDocumentHolder
    {
        private readonly Func<TDocument> documentFactory;

        public DocumentHolderQueryExecutor(Directory directory, Context context, Func<TDocument> documentFactory) : base(directory, context)
        {
            this.documentFactory = documentFactory;
        }

        protected override void  SetCurrentDocument(Document doc)
        {
            var holder = documentFactory();
            holder.Document = doc;

            CurrentDocument = holder;
        }
    }

    internal abstract class LuceneQueryExecutor<TDocument> : IQueryExecutor
    {
        private readonly Directory directory;
        private readonly Context context;

        public TDocument CurrentDocument { get; protected set; }

        protected LuceneQueryExecutor(Directory directory, Context context)
        {
            this.directory = directory;
            this.context = context;
        }

        public T ExecuteScalar<T>(QueryModel queryModel)
        {
            return default(T);
        }

        public T ExecuteSingle<T>(QueryModel queryModel, bool returnDefaultWhenEmpty)
        {
            var sequence = ExecuteCollection<T>(queryModel);

            return returnDefaultWhenEmpty ? sequence.SingleOrDefault() : sequence.Single();
        }

        public IEnumerable<T> ExecuteCollection<T>(QueryModel queryModel)
        {
            QueryModelTransformer.TransformQueryModel(queryModel);

            var builder = new QueryModelTranslator(context);
            builder.Build(queryModel);

#if DEBUG
            System.Diagnostics.Trace.WriteLine("Lucene query: " + builder.Query + " sort: " + builder.Sort, "Lucene.Net.Linq");
#endif

            var mapping = new QuerySourceMapping();
            mapping.AddMapping(queryModel.MainFromClause, GetCurrentRowExpression());
            queryModel.TransformExpressions(e => ReferenceReplacingExpressionTreeVisitor.ReplaceClauseReferences(e, mapping, true));

            var projection = GetProjector<T>(queryModel);
            var projector = projection.Compile();

            using (var searcher = new IndexSearcher(directory, true))
            {
                var hits = searcher.Search(builder.Query, null, searcher.MaxDoc(), builder.Sort);

                foreach (var hit in hits.ScoreDocs)
                {
                    SetCurrentDocument(searcher.Doc(hit.doc));
                    yield return projector(CurrentDocument);
                }
            }
        }

        protected abstract void SetCurrentDocument(Document doc);

        protected virtual Expression GetCurrentRowExpression()
        {
            return Expression.Property(Expression.Constant(this), "CurrentDocument");
        }

        protected virtual Expression<Func<TDocument, T>> GetProjector<T>(QueryModel queryModel)
        {
            return Expression.Lambda<Func<TDocument, T>>(queryModel.SelectClause.Selector, Expression.Parameter(typeof(TDocument)));
        }
    }
}