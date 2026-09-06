namespace JobLinkv2.Repositories
{
    public interface IGenericRepository<T> where T : class
    {
        public T GetById(object id);

        public IEnumerable<T> GetAll();

        public bool Add(T entity);

        public bool Update(T entity);

        public bool Delete(int id);
    }
}