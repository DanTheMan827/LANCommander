﻿using LANCommander.Data;
using LANCommander.Data.Models;
using System.Linq.Expressions;

namespace LANCommander.Services
{
    public abstract class BaseDatabaseService<T> where T : BaseModel
    {
        public DatabaseContext Context { get; set; }
        public HttpContext HttpContext { get; set; }

        public BaseDatabaseService(DatabaseContext dbContext, IHttpContextAccessor httpContextAccessor) {
            Context = dbContext;
            HttpContext = httpContextAccessor.HttpContext;
        }

        public virtual ICollection<T> Get()
        {
            return Get(x => true).ToList();
        }

        public virtual async Task<T> Get(Guid id)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                return await repo.Find(id);
            }
        }

        public virtual IQueryable<T> Get(Expression<Func<T, bool>> predicate)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                return repo.Get(predicate);
            }
        }

        public virtual bool Exists(Guid id)
        {
            return Get(id) != null;
        }

        public virtual async Task<T> Add(T entity)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                entity = await repo.Add(entity);
                await repo.SaveChanges();

                return entity;
            }
        }

        /// <summary>
        /// Adds an entity to the database if it does exist as dictated by the predicate
        /// </summary>
        /// <param name="predicate">Qualifier expressoin</param>
        /// <param name="entity">Entity to add</param>
        /// <returns>Newly created or existing entity</returns>
        public virtual async Task<T> AddMissing(Expression<Func<T, bool>> predicate, T entity)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                var existing = repo.Get(predicate).FirstOrDefault();

                if (existing == null)
                {
                    entity = await repo.Add(entity);

                    await repo.SaveChanges();

                    return entity;
                }
                else
                {
                    return existing;
                }
            }
        }

        public virtual async Task<T> Update(T entity)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                var existing = await repo.Find(entity.Id);

                Context.Entry(existing).CurrentValues.SetValues(entity);

                entity = repo.Update(existing);
                await repo.SaveChanges();

                return entity;
            }
        }

        public virtual async Task Delete(T entity)
        {
            using (var repo = new Repository<T>(Context, HttpContext))
            {
                repo.Delete(entity);
                await repo.SaveChanges();
            }
        }
    }
}
